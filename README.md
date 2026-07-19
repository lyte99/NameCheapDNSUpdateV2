# NameChapDNSUpdateV2

## Overview

NameChapDNSUpdateV2 is a .NET console application that keeps a NameCheap DNS record pointed at your
current public IP. It checks at a configurable interval and calls NameCheap's Dynamic DNS endpoint
whenever the public IP changes.

## Features

- Automatic DNS updates for one or more hosts on a domain
- Environment variable based configuration
- Runs as a long-lived process, suitable for Docker

## Prerequisites

- .NET 8 SDK
- A NameCheap account with the domain's Dynamic DNS enabled

## Installation

```bash
git clone https://github.com/lyte99/NameChapDNSUpdateV2.git
cd NameChapDNSUpdateV2
```

## Configuration

All configuration comes from environment variables. All four are required.

| Variable | Description | Example |
| --- | --- | --- |
| `domain` | Fully qualified domain to update | `example.com` |
| `hosts` | Hosts to update, separated by semicolons | `@;www` |
| `dynamicDNSPassword` | Dynamic DNS password from NameCheap | `abc123...` |
| `intCheckTimerSEC` | Seconds between checks, 1 to 86400 | `300` |

The application validates all four at startup and exits with a non-zero status if any are missing
or malformed.

`dynamicDNSPassword` is a credential. Pass it in at run time and keep it out of the image, out of
source control, and out of any shell history you keep around.

## Usage

```bash
export domain=example.com
export hosts="@;www"
export dynamicDNSPassword=your-dynamic-dns-password
export intCheckTimerSEC=300

dotnet run
```

### Docker

`docker-compose.yml` reads the same four variables from your environment or a local `.env` file
(which is git-ignored):

```bash
docker compose up -d --build
```

## How it works

On startup the application resolves the first configured host to learn the IP currently published
in DNS. From then on it tracks the IP it has published itself rather than re-querying DNS each
cycle, because a freshly updated record keeps returning the old value until its TTL expires. If an
update fails, the published IP is treated as unknown and the update is retried on the next cycle.

Only IPv4 (`A` record) addresses are considered. A host that also carries an `AAAA` record is
handled correctly.

## Contributing

Feel free to open issues or submit pull requests. All contributions are welcome.

## License

MIT.
