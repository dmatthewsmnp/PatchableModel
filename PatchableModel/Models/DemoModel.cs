namespace PatchableModel.Models;

public class DemoModel : UpdateableModel, IValidatableObject
{
	public Guid id { get; init; }

	[Updateable]
	[Required(AllowEmptyStrings = false, ErrorMessage = "This is required")]
	public string? name { get; set; }

	[Updateable]
	public int? no { get; set; } = 77;

	[Updateable]
	public DemoModel? nested { get; set; }

	public DateTimeOffset lastUpdateDateTime { get; set; } = DateTimeOffset.UtcNow;

	public override void OnModelUpdated(List<string> updatedProperties)
		=> lastUpdateDateTime = DateTimeOffset.UtcNow;

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (name == "InvalidName")
		{
			yield return new ValidationResult("Name is InvalidName", new[] { nameof(name) });
		}
	}
}
