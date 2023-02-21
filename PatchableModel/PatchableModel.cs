using System.Reflection;

namespace PatchableModel;

public abstract class PatchableModel
{
	/// <summary>
	/// Flag for whether to continue through Patch operation when validation error occurs (true, default) and return
	/// validation results for all invalid properties, or to halt immediately at first validation failure (false)
	/// </summary>
	protected bool _validateAllProperties { get; init; }

	/// <summary>
	/// Optional post-update callback method, in case any steps need to be performed (i.e. updating metadata) prior to saving updated object
	/// </summary>
	public virtual void OnChange() { }

	/// <summary>
	/// Patch method - apply sourceDocument to properties in this object marked Patchable
	/// </summary>
	public PatchResult Patch(JsonDocument sourceDocument)
	{
		// Build collection of properties in this object which have Patchable attribute:
		var patchableProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(prop => prop.CustomAttributes.Any(attr => attr.AttributeType == typeof(PatchableAttribute)))
			.ToDictionary(prop => prop.Name, prop => prop);
		if (patchableProperties.Count == 0)
		{
			return new PatchResult.NoChanges();
		}

		// Build collection of properties from incoming document which align with patchable properties:
		var sourceProperties = sourceDocument.RootElement.EnumerateObject()
			.Where(je => patchableProperties.ContainsKey(je.Name))
			.ToDictionary(prop => prop.Name, prop => prop.Value);
		if (sourceProperties.Count == 0)
		{
			return new PatchResult.NoChanges();
		}

		var updatedProperties = new List<string>();
		var patchErrors = new List<ValidationResult>();
		var nullabilityContext = new NullabilityInfoContext();

		// Iterate through collection of properties to be updated:
		foreach (var kvp in sourceProperties)
		{
			// If any validation errors have occurred, break unless we are configured to validate all properties:
			if (patchErrors.Count > 0 && !_validateAllProperties)
			{
				break;
			}

			// Retrieve target property for source value, and any validation attributes:
			var targetProp = patchableProperties[kvp.Key];
			var validationAttrs = targetProp.GetCustomAttributes<ValidationAttribute>();

			// Check for explicitly-null incoming value:
			if (kvp.Value.ValueKind == JsonValueKind.Null)
			{
				// Ensure target property does not have Required attribute:
				var requiredAttribute = validationAttrs.FirstOrDefault(attr => attr is RequiredAttribute);
				if (requiredAttribute is not null)
				{
					patchErrors.Add(new(requiredAttribute.ErrorMessage, new[] { kvp.Key }));
				}
				// Ensure target property is nullable:
				else if (nullabilityContext.Create(targetProp).WriteState != NullabilityState.Nullable)
				{
					patchErrors.Add(new("Value must not be null", new[] { kvp.Key }));
				}
				// If target is not already null, update now:
				else if (targetProp.GetValue(this) is not null)
				{
					targetProp.SetValue(this, null);
					updatedProperties.Add(kvp.Key);
				}
				continue;
			}

			try
			{
				// Attempt to deserialize incoming value into required target type (note this will throw JsonException
				// if type conversion fails):
				var sourceValue = kvp.Value.Deserialize(targetProp.PropertyType);

				// Run validation against incoming value for any validation attributes on target property:
				bool validationError = false;
				foreach (var validationAttr in validationAttrs)
				{
					if (!validationAttr.IsValid(sourceValue))
					{
						patchErrors.Add(new(validationAttr.ErrorMessage, new[] { kvp.Key }));
						validationError = true;
					}
				}

				// If validation did not fail, compare target property to incoming value, and update if required (note that
				// reference types will always meet != criteria, meaning they will be updated even if equivalent):
				if (!validationError && targetProp.GetValue(this) != sourceValue)
				{
					targetProp.SetValue(this, sourceValue);
					updatedProperties.Add(kvp.Key);
				}
			}
			catch (JsonException)
			{
				patchErrors.Add(new("Error deserializing value", new[] { kvp.Key }));
			}
		}

		// If there were errors updating properties, return error result:
		if (patchErrors.Count > 0)
		{
			return new PatchResult.Error(patchErrors);
		}
		// If no fields were updated, skip post-validation and return no-changes:
		if (updatedProperties.Count == 0)
		{
			return new PatchResult.NoChanges();
		}

		// Run validation against ending object state:
		var endingValidationContext = new ValidationContext(this);
		if (!Validator.TryValidateObject(this, endingValidationContext, patchErrors))
		{
			return new PatchResult.Error(patchErrors);
		}

		// Run OnChange method, so child class can perform any required activities:
		OnChange();

		// Return success, with list of modified properties:
		return new PatchResult.Ok(updatedProperties);
	}
}
