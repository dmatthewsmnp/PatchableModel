namespace PatchableModel;

/// <summary>
/// Half-assed version of discriminated union
/// </summary>
public record PatchResult
{
	public record Ok(IEnumerable<string> UpdatedProperties) : PatchResult { }
	public record Error(IEnumerable<ValidationResult> ValidationResults) : PatchResult { }
	public record NoChanges() : PatchResult { }

	private PatchResult() { } // Prevent types being defined outside this class
}
