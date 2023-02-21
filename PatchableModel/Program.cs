using PatchableModel;
using PatchableModel.Models;

// Set up dummy datasource with some objects:
var mockDatasource = new Dictionary<Guid, DemoModel>();
for (int i =0; i < 10; i++)
{
	var id = Guid.NewGuid();
	mockDatasource[id] = new DemoModel { id = id, name = i.ToString() };
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

// Map GET endpoints:
app.MapGet("/demomodels", () => Results.Ok(mockDatasource.Values)).WithName("GetDemoModels");
app.MapGet("/demomodels/{id:guid}", (Guid id) => mockDatasource.ContainsKey(id) ? Results.Ok(mockDatasource[id]) : Results.NotFound()).WithName("GetDemoModel");

// Map POST/PUT/PATCH endpoints:
app.MapPost("/demomodels", async (HttpRequest request, CancellationToken cancellationToken) =>
{
	using var jsonDocument = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);

	// Create new object (generating id), apply incoming property values:
	var model = new DemoModel { id = Guid.NewGuid() };
	var patchResult = model.UpdateModel(jsonDocument, HttpMethod.Post);
	if (patchResult is UpdateResult.Error error)
	{
		return Results.BadRequest(error.ValidationResults); // Would be ProblemDetails
	}
	if (patchResult is UpdateResult.Ok || patchResult is UpdateResult.NoChanges)
	{
		mockDatasource[model.id] = model; // Would really save to repository
		return Results.Created($"{request.Path}/{model.id}", model);
	}
	return Results.StatusCode(StatusCodes.Status500InternalServerError); // Would be ProblemDetails
}).WithName("PostDemoModel").Accepts<DemoModel>("application/json");

app.MapPut("/demomodels/{id:guid}", async (HttpRequest request, Guid id, CancellationToken cancellationToken) =>
{
	using var jsonDocument = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);

	// Look up existing object (would really be from repository), create new if required (with provided id), apply incoming property values:
	var model = mockDatasource.ContainsKey(id) ? mockDatasource[id] : new DemoModel { id = id };
	var patchResult = model.UpdateModel(jsonDocument, HttpMethod.Put);
	if (patchResult is UpdateResult.Error error)
	{
		return Results.BadRequest(error.ValidationResults); // Would be ProblemDetails
	}
	if (patchResult is UpdateResult.Ok || patchResult is UpdateResult.NoChanges)
	{
		// This would really save to repository and return result...
		if (mockDatasource.ContainsKey(id))
		{
			mockDatasource[model.id] = model;
			return Results.Ok(model);
		}
		mockDatasource[model.id] = model;
		return Results.Created($"{request.Path}/{model.id}", model);
	}
	return Results.StatusCode(StatusCodes.Status500InternalServerError); // Would be ProblemDetails
}).WithName("PutDemoModel").Accepts<DemoModel>("application/json");

app.MapPatch("/demomodels/{id:guid}", async (HttpRequest request, Guid id, CancellationToken cancellationToken) =>
{
	using var jsonDocument = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);

	// Look up existing object (would really be from repository), return 404 if not found:
	if (!mockDatasource.TryGetValue(id, out var model))
	{
		return Results.NotFound(); // Would be ProblemDetails
	}
	var patchResult = model.UpdateModel(jsonDocument, HttpMethod.Patch);
	if (patchResult is UpdateResult.Error error)
	{
		return Results.BadRequest(error.ValidationResults); // Would be ProblemDetails
	}
	if (patchResult is UpdateResult.Ok || patchResult is UpdateResult.NoChanges)
	{
		mockDatasource[model.id] = model; // Would really save to repository
		return Results.Ok(model);
	}
	return Results.StatusCode(StatusCodes.Status500InternalServerError); // Would be ProblemDetails
}).WithName("PatchDemoModel").Accepts<DemoModel>("application/json");

app.Run();
