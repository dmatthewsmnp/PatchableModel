namespace PatchableModel.Models;

public class DemoModel : PatchableModel
{
	public Guid id { get; init; }

	[Patchable]
	public string? name { get; set; }

	public DateTimeOffset lastUpdateDateTime { get; set; } = DateTimeOffset.UtcNow;

	public override void OnChange()
		=> lastUpdateDateTime = DateTimeOffset.UtcNow;
}
