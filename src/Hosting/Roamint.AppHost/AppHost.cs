var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume("roamint-pgdata")
    .WithPgAdmin();

var roamintdb = postgres.AddDatabase("roamintdb");

builder.AddProject<Projects.Roamint_Web>("web")
    .WithReference(roamintdb)
    .WaitFor(roamintdb);

builder.Build().Run();
