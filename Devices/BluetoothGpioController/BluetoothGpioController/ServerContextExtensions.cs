using DotnetBleServer.Advertisements;
using DotnetBleServer.Core;
using DotnetBleServer.Gatt;
using DotnetBleServer.Gatt.BlueZModel;
using DotnetBleServer.Gatt.Description;
using Microsoft.Extensions.Options;

namespace BluetoothGpioController;

public static class ServerContextExtensions
{
	public static async Task RegisterAdvertisement(this ServerContext context, string name)
	{
		var advertisementProperties = new LEAdvertisement1Properties
		{
			Type = "peripheral",
			ServiceUUIDs = ["12345678-1234-5678-1234-56789abcdef0"],
			LocalName = name,
			Appearance = 128, // (ushort)Convert.ToUInt32("0x0080", 16),
			Discoverable = true,
			IncludeTxPower = true
		};

		await new AdvertisingManager(context).CreateAdvertisement(advertisementProperties);
	}

	public static async Task RegisterGattApplication(this ServerContext context, ILoggerFactory loggerFactory, IOptions<AppSettings> options)
	{
		var gattServiceDescription = new GattServiceDescription
		{
			UUID = "12345678-1234-5678-1234-56789abcdef0",
			Primary = true
		};

		var gattCharacteristicDescription = new GattCharacteristicDescription(loggerFactory, options)
		{
			UUID = "12345678-1234-5678-1234-56789abcdef1",
			Flags = CharacteristicFlags.Notify
		};

		var gattDescriptorDescription = new GattDescriptorDescription
		{
			Value = [(byte)'t'],
			UUID = "12345678-1234-5678-1234-56789abcdef2",
			Flags = ["read", "write"]
		};

		var gab = new GattApplicationBuilder();
		gab
			.AddService(gattServiceDescription)
			.WithCharacteristic(gattCharacteristicDescription, [gattDescriptorDescription]);

		await new GattApplicationManager(context).RegisterGattApplication(gab.BuildServiceDescriptions());
	}
}