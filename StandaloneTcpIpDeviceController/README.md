# Standalone TCP/IP Device Controller

Small dependency-free Python tool for quick TCP/IP device command testing.

## GUI

```powershell
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py
```

Use `Connect`, type a command, then:

- `Send`: sends a command without reading.
- `Query`: sends and reads one response.
- `Read`: reads a pending response.
- `Run Script`: runs a text file of commands. Lines ending in `?` are treated as queries.

Default device address is `10.128.1.176:51123`.

## CLI

```powershell
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --query "*IDN?"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --command "OUTP ON"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --interactive
```

Terminator options:

```powershell
--terminator LF
--terminator CR
--terminator CRLF
--terminator None
```
