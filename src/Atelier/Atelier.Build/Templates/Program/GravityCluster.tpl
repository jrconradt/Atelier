var capabilities = EventHorizon.Cluster.NodeCapabilities.Compute;

{{ capabilityAssignments }}

builder.Services.AddGravityCluster(port: gravityPort, capabilities: capabilities);
