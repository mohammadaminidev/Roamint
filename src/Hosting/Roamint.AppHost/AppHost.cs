var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume("roamint-pgdata")
    // Fixed host port + persistent container so the phone-facing standalone host
    // (scripts/phone.ps1) has a deterministic connection string and can run
    // without the AppHost. See docs/features/0014-phone-lan-access.md.
    .WithHostPort(15432)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var roamintdb = postgres.AddDatabase("roamintdb");

builder.AddProject<Projects.Roamint_Web>("web")
    .WithReference(roamintdb)
    .WaitFor(roamintdb);

builder.Build().Run();
