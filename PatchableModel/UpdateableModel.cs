﻿using System.Reflection;

namespace PatchableModel;

public abstract class UpdateableModel
{
	internal const string RequiredPropertyErrorMessage = "Value must not be null";
	internal const string PropertyDeserializationErrorMessage = "Error deserializing value";
	private static readonly JsonElement _nullElement = JsonSerializer.SerializeToElement<object?>(null);

	/// <summary>
	/// Flag for whether to continue through UpdateModel operation when validation error occurs (true, default) and return
	/// validation results for all invalid properties, or to halt immediately at first validation failure (false)
	/// </summary>
	protected virtual bool _validateAllProperties { get; init; }

	/// <summary>
	/// Optional post-update callback method, in case any steps need to be performed (i.e. updating metadata) prior to saving updated object
	/// </summary>
	public virtual void OnModelUpdated() { }

	/// <summary>
	/// UpdateModel method - apply sourceDocument to properties in this object marked Updateable
	/// </summary>
	public UpdateResult UpdateModel(JsonDocument sourceDocument, HttpMethod httpMethod)
	{
		// Build collection of properties in this object which have Updateable attribute:
		var updateableProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(prop => prop.CustomAttributes.Any(attr => attr.AttributeType == typeof(UpdateableAttribute)))
			.ToDictionary(prop => prop.Name, prop => prop);
		if (updateableProperties.Count == 0)
		{
			return new UpdateResult.NoChanges();
		}

		// Build collection of properties from incoming document which align with Updateable properties:
		var sourceProperties = sourceDocument.RootElement.EnumerateObject()
			.Where(je => updateableProperties.ContainsKey(je.Name))
			.ToDictionary(prop => prop.Name, prop => prop.Value);

		// If this is a PATCH operation and there are no properties to update, return immediately:
		if (httpMethod == HttpMethod.Patch && sourceProperties.Count == 0)
		{
			return new UpdateResult.NoChanges();
		}

		// If this is a PUT, all updateable properties should be included (where a POST could leave as default); add
		// explicit null values for any properties that are not in source JSON:
		if (httpMethod == HttpMethod.Put)
		{
			foreach (var missingKey in updateableProperties.Keys.Where(key => !sourceProperties.ContainsKey(key)))
			{
				sourceProperties[missingKey] = _nullElement;
			}
		}

		var updatedProperties = new List<string>();
		var validationErrors = new List<ValidationResult>();
		var nullabilityContext = new NullabilityInfoContext();

		#region Iterate through sourceProperties collection, applying updates
		foreach (var kvp in sourceProperties)
		{
			// If any validation errors have occurred, break (unless we are configured to validate all properties):
			if (validationErrors.Count > 0 && !_validateAllProperties)
			{
				break;
			}

			// Retrieve target property for source value, and any attached validation attributes:
			var targetProp = updateableProperties[kvp.Key];
			var validationAttrs = targetProp.GetCustomAttributes<ValidationAttribute>();

			// Check for explicitly-null incoming value:
			if (kvp.Value.ValueKind == JsonValueKind.Null)
			{
				// Ensure target property does not have Required attribute:
				var requiredAttribute = validationAttrs.FirstOrDefault(attr => attr is RequiredAttribute);
				if (requiredAttribute is not null)
				{
					validationErrors.Add(new(requiredAttribute.ErrorMessage, new[] { kvp.Key }));
				}
				// Ensure target property is nullable:
				else if (nullabilityContext.Create(targetProp).WriteState != NullabilityState.Nullable)
				{
					validationErrors.Add(new(RequiredPropertyErrorMessage, new[] { kvp.Key }));
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
				// Attempt to deserialize incoming value into required target type (note this will throw a
				// JsonException if type conversion fails):
				var sourceValue = kvp.Value.Deserialize(targetProp.PropertyType);

				// Run all validation attributes on target property against incoming value:
				bool validationError = false;
				foreach (var validationAttr in validationAttrs)
				{
					if (!validationAttr.IsValid(sourceValue))
					{
						validationErrors.Add(new(validationAttr.ErrorMessage, new[] { kvp.Key }));
						validationError = true;
					}
				}

				// If validation did not fail, compare target property to incoming value and update if required (note that
				// reference types will always meet != criteria, meaning they will be updated even if equivalent):
				if (!validationError && targetProp.GetValue(this) != sourceValue)
				{
					targetProp.SetValue(this, sourceValue);
					updatedProperties.Add(kvp.Key);
				}
			}
			catch (JsonException)
			{
				validationErrors.Add(new(PropertyDeserializationErrorMessage, new[] { kvp.Key }));
			}
		}
		#endregion

		// If there were errors updating properties, return error result:
		if (validationErrors.Count > 0)
		{
			return new UpdateResult.Error(validationErrors);
		}
		// If this is a patch and no fields were updated, skip post-validation and return no-changes:
		if (httpMethod == HttpMethod.Patch && updatedProperties.Count == 0)
		{
			return new UpdateResult.NoChanges();
		}

		// Run validation against ending object state:
		var endingValidationContext = new ValidationContext(this);
		if (!Validator.TryValidateObject(this, endingValidationContext, validationErrors))
		{
			return new UpdateResult.Error(validationErrors);
		}

		// Run OnModelUpdated method, so child class can perform any required activities:
		OnModelUpdated();

		// Return success, with list of modified properties:
		return new UpdateResult.Ok(updatedProperties);
	}
}
