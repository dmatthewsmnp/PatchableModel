namespace PatchableModel.Models;

public class DemoModel : PatchableModel, IValidatableObject
{
	public Guid id { get; init; }

	[Patchable]
	[Required(AllowEmptyStrings = false, ErrorMessage = "This is required")]
	public string? name { get; set; }

	[Patchable]
	public int no { get; set; }

	public DateTimeOffset lastUpdateDateTime { get; set; } = DateTimeOffset.UtcNow;

	public override void OnChange()
		=> lastUpdateDateTime = DateTimeOffset.UtcNow;

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (name == "InvalidName")
		{
			yield return new ValidationResult("Name is InvalidName", new[] { nameof(name) });
		}
	}
}
