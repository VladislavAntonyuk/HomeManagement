using System.Device.Gpio;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace BluetoothGpioController;

internal class GattCharacteristicDescription(ILoggerFactory loggerFactory, IOptions<AppSettings> options) : DotnetBleServer.Gatt.Description.GattCharacteristicDescription
{
    private const string Help = """
                                PASSWORD;GPIO;PIN;MODE;VALUE
                                PASSWORD;REBOOT
                                PASSWORD;SHUTDOWN
                                """;
    
    private readonly ILogger<GattCharacteristicDescription> _logger = loggerFactory.CreateLogger<GattCharacteristicDescription>();

    public override Task WriteValueAsync(byte[] value)
    {
        var command = Encoding.UTF8.GetString(value);
        _logger.LogInformation("Received value: {Value}", command);

        var splitCommand = command.Split(';');
        if (splitCommand.Length < 2)
        {
            _logger.LogError("No command received");
            return base.WriteValueAsync(value);
        }

        if (splitCommand[0] != options.Value.Password)
        {
            _logger.LogError("Invalid password");
            return base.WriteValueAsync(value);
        }

        switch (splitCommand[1])
        {
            case "GPIO":
                if (!ProcessGpio(splitCommand))
                {
                    _logger.LogError("Failed to process GPIO command");
                }

                break;
            case "REBOOT":
                _logger.LogInformation("Reboot command received. Rebooting system...");
                if (!Reboot())
                {
                    _logger.LogError("Failed to process REBOOT command");
                }

                break;
            case "SHUTDOWN":
                _logger.LogInformation("Shutdown command received.");
                if (!Shutdown())
                {
                    _logger.LogError("Failed to process SHUTDOWN command");
                }

                break;
            default:
                _logger.LogWarning("Unknown command: {Command}", command);
                break;
        }



        return base.WriteValueAsync(value);
    }

    public override Task<byte[]> ReadValueAsync()
    {
        using var controller = new GpioController();
        for (var pinNumber = 1; pinNumber < controller.PinCount; pinNumber++)
        {
            var pin = controller.OpenPin(pinNumber);
            var mode = pin.GetPinMode();
            var pinValue = pin.Read();
            _logger.LogInformation("Reading value PIN: {Pin}, MODE: {Mode}, VALUE: {Value}", pinNumber, mode, pinValue);
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(Help));
    }

    private bool ProcessGpio(string[] gpioCommand)
    {
        if (gpioCommand.Length != 5)
        {
            _logger.LogError("Invalid command format. Expected: PASSWORD;GPIO;PIN;MODE;VALUE");
            return false;
        }

        var pinNumber = Convert.ToInt32(gpioCommand[2]);
        using var controller = new GpioController();
        var pin = controller.OpenPin(pinNumber, Enum.Parse<PinMode>(gpioCommand[3], true));
        pin.Write(Convert.ToInt32(gpioCommand[4]));
        return true;
    }

    private bool Reboot()
    {
        try
        {
            Process.Start("sudo", "systemctl reboot");
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
        }

        return false;
    }

    private bool Shutdown()
    {
        try
        {
            Process.Start("sudo", "systemctl poweroff");
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
        }

        return false;
    }
}