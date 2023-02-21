namespace PatchableModel;

/// <summary>
/// Half-assed version of discriminated union
/// </summary>
public record UpdateResult
{
	public record Ok(IEnumerable<string> UpdatedProperties) : UpdateResult { }
	public record Error(IEnumerable<ValidationResult> ValidationResults) : UpdateResult { }
	public record NoChanges() : UpdateResult { }

	private UpdateResult() { } // Prevent types being defined outside this class
}
