var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume("roamint-pgdata")
    // Persistent container so the data + password survive across AppHost runs and the
    // phone-facing standalone host (scripts/phone*.ps1) can reuse it without the AppHost.
    // NOTE: WithHostPort only pins the *DCP proxy* port (used while the AppHost runs); the
    // container itself is published on a random host port. The standalone scripts therefore
    // read the live port from `docker port` rather than relying on 15432.
    // See docs/features/0014-phone-lan-access.md.
    .WithHostPort(15432)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var roamintdb = postgres.AddDatabase("roamintdb");

builder.AddProject<Projects.Roamint_Web>("web")
    .WithReference(roamintdb)
    .WaitFor(roamintdb);

builder.Build().Run();
