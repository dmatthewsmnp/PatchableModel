using System.Reflection;

namespace PatchableModel;

public abstract class PatchableModel
{
	/// <summary>
	/// Optional pre-validation method: test whether incoming Json properties are valid
	/// </summary>
	/// <param name="sourceProperties">Dictionary of property names with the corresponding incoming JsonElement</param>
	/// <returns>An empty collection if ValidationResults if the request is valid, a non-empty collection if it is not (will abort request)</returns>
	public virtual IEnumerable<ValidationResult> PreValidate(Dictionary<string, JsonElement> sourceProperties)
		=> Array.Empty<ValidationResult>();

	/// <summary>
	/// Optional post-validation method: test whether post-update object is valid
	/// </summary>
	/// <returns>An empty collection if ValidationResults if the ending state is valid, a non-empty collection if it is not (will abort save of object)</returns>
	public virtual IEnumerable<ValidationResult> PostValidate()
		=> Array.Empty<ValidationResult>();

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

		// Run pre-validation against incoming document:
		var preValidationResults = PreValidate(sourceProperties);
		if (preValidationResults.Any())
		{
			return new PatchResult.Error(preValidationResults);
		}

		// Iterate through collection of properties to be updated:
		var updatedProperties = new List<string>();
		var patchErrors = new List<ValidationResult>();
		var nullabilityContext = new NullabilityInfoContext();
		foreach (var kvp in sourceProperties)
		{
			var targetProp = patchableProperties[kvp.Key];
			if (kvp.Value.ValueKind == JsonValueKind.Null)
			{
				// Incoming value is present and explicitly null; ensure target property is nullable:
				if (nullabilityContext.Create(targetProp).WriteState != NullabilityState.Nullable)
				{
					patchErrors.Add(new("Value must not be null", new[] { kvp.Key })); // Note: could break here to save time, unless caller wants all property results
				}
				else if (targetProp.GetValue(this) is not null)
				{
					targetProp.SetValue(this, null);
					updatedProperties.Add(kvp.Key);
				}
				continue;
			}
			try
			{
				var sourceValue = kvp.Value.Deserialize(targetProp.PropertyType);
				if (targetProp.GetValue(this) != sourceValue) // Note: will always be true for reference types
				{
					targetProp.SetValue(this, sourceValue);
					updatedProperties.Add(kvp.Key);
				}
			}
			catch (JsonException)
			{
				patchErrors.Add(new("Error deserializing value", new[] { kvp.Key })); // Note: could break here to save time, unless caller wants all property results
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

		// Run post-validation:
		var postValidationResults = PostValidate();
		if (postValidationResults.Any())
		{
			return new PatchResult.Error(postValidationResults);
		}

		// Run OnChange method, so child class can perform any required activities:
		OnChange();

		// Return success, with list of modified properties:
		return new PatchResult.Ok(updatedProperties);
	}
}
