var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.AspireSample_Api>("api");

builder.Build().Run();
