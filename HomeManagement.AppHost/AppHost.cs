var builder = DistributedApplication.CreateBuilder(args);

var mqtt = builder.AddMosquitto("mqtt")
    .WithDataVolume();

var hub = builder.AddDotnetProject("homemanagement", "../HomeManagement/HomeManagement.csproj")
    .WithReference(mqtt)
    .WaitFor(mqtt);

builder.AddDotnetProject("homemanagement-client", "../HomeManagement.Client/HomeManagement.Client.csproj")
    .WithExplicitStart()
    .WaitFor(hub);

builder.AddDotnetProject("homemanagement-devices-processdevice", "../Devices/HomeManagement.Devices.ProcessDevice/HomeManagement.Devices.ProcessDevice.csproj")
    .WithReference(mqtt)
    .WithExplicitStart()
    .WaitFor(hub);

builder.AddDotnetProject("homemanagement-devices-tuyaplug", "../Devices/HomeManagement.Devices.TuyaPlug/HomeManagement.Devices.TuyaPlug.csproj")
    .WithReference(mqtt)
    .WithExplicitStart()
    .WaitFor(hub);

builder.Build().Run();
