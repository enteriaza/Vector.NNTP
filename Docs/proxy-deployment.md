# HAProxy PROXY deployment modes

## Recommended: PROXY only behind a load balancer

Place `Vector.NNRPD` **behind HAProxy (or another PROXY-capable proxy)** on a private network path. Configure `ProxyProtocol:TrustedSources` to match the **HAProxy instance IP addresses** (or their management subnet) using exact hosts or CIDR ranges.

In this mode:

- The TCP `RemoteEndPoint` seen by the process is the load balancer.
- When `ProxyProtocol:Enabled` is `true` and the peer is trusted, the server parses PROXY and sets the effective **client** endpoint used for limits, logs, and metrics (`NntpConnectionContext.ClientRemoteEndPoint`).
- `NntpConnectionContext.ProxyHopEndPoint` retains the TCP peer (the load balancer) for correlation.

## Discouraged: PROXY on the public Internet

If clients can reach the NNTP port **without** passing through your controlled proxy, a remote party can attempt to spoof PROXY headers. With `ProxyProtocol:StrictTrustedSourcesOnly` set to `true` (the default), a successfully parsed PROXY header from a **non-trusted** peer is treated as an error.

For public listener ports, prefer **disabling** `ProxyProtocol:Enabled` unless you fully understand the trust implications.

## Strict vs permissive

- **Strict (`StrictTrustedSourcesOnly: true`)**: a parsed PROXY header from an untrusted first hop is rejected.
- **Non-strict**: not recommended on edge deployments; only evaluate on fully trusted networks.

See `docs/nnrpd-json.md` for host configuration; listener and `Server:ProxyProtocol` options are under the `Server` section in `nnrpd.json`.
