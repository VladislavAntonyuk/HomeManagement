# Bluetooth GPIO Controller

Switches a relay on a Raspberry Pi GPIO pin over Bluetooth.

## What you need

- A Raspberry Pi, any model with GPIO pins.
- A [relay module](https://radiostore.com.ua/ua/p229350226-modul-rele-kanalnyj.html), 5V or 3.3V.

## Wiring

### Relay to Raspberry Pi

| Relay pin | Raspberry Pi pin |
|---|---|
| VCC | 5V, or 3.3V if the module supports it |
| GND | GND |
| IN | A GPIO pin, for example GPIO17 |

### Relay terminals

The COM, NO and NC terminals switch the power of the device, for example a light.

| Terminal | Meaning |
|---|---|
| COM (Common) | The main terminal where the power comes in. |
| NO (Normally Open) | Open while the relay is off, closed while it is on. Use it to turn the device on with the relay. |
| NC (Normally Closed) | Closed while the relay is off, open while it is on. Use it to turn the device off with the relay. |

### Relay to a light socket

| From | To |
|---|---|
| Live wire from the power source | COM |
| NO | Live wire to the light |
| Neutral wire | Directly to the light |

> [!WARNING]
> Mains voltage is dangerous.
> Turn off the power before wiring.

## Pairing the Bluetooth device

On the Raspberry Pi:

```bash
rfkill unblock bluetooth
bluetoothctl
```

Then in `bluetoothctl`:

```text
power on
agent on
default-agent
scan on
pair <MAC>
trust <MAC>
connect <MAC>
```

## Deploy

Publish the `BluetoothGpioController` project for `linux-arm64`, then copy it to the Raspberry Pi and make it executable:

```bash
scp -r "linux-arm64/*.*" <user>@raspberrypi-zero-2w.local:~/Projects/BluetoothGpioController
ssh <user>@raspberrypi-zero-2w.local
chmod +x ~/Projects/BluetoothGpioController/BluetoothGpioController
chmod +x ~/Projects/BluetoothGpioController/BluetoothGpioController.dll
```
