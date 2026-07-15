var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FortuneForge_Server>("fortuneforge-server");

builder.Build().Run();
