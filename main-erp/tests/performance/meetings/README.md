# Hadron meeting qualification

This runner requires **Windows and PowerShell 7 or later**. It is not the Linux production deployment harness.

This Windows-only harness separates claims that must never be confused:

1. **ERP control-plane component test** exercises 50 distinct Testing identities and 100 distinct one-use guest invitations through selected Hadron meeting lifecycle endpoints.
2. **Media-plane component test** uses the official LiveKit load tester to create synthetic WebRTC publications, subscriptions and connections.
3. **Final qualification** additionally requires browser QoE measurements, external-device UAT, UDP/TCP/TURN coverage, human accessibility review and no open P0/P1 defect. The local harness does not invent or infer these approvals.

The ASP.NET integration tests use the `Testing` authentication handler and a fake media gateway. They do **not** test production cookie login, 150 simultaneous human sessions or real media. Requests are sent in batches with a maximum HTTP concurrency of 25; therefore the tests prove identity cardinality, guest uniqueness, waiting-room/attendance behavior and lifecycle correctness, not 150-request concurrency.

The LiveKit CLI bypasses Hadron Identity, permissions and guest-invitation APIs. Its participant count proves only media-server behavior. It cannot by itself prove ERP admission correctness or human audio/video quality.

## Evidence rules

Every invocation receives a uniquely addressed directory under `artifacts/performance/meetings/<run-id>/`. The directory is not claimed to be filesystem-immutable. Evidence records:

- component results and final-qualification gates;
- exact TRX identity and outcome for both scale tests;
- sanitized logs and resource samples;
- the Git revision plus dirty-state counts without diff contents;
- SHA-256 for relevant source/test files, the thresholds snapshot and available executables;
- tool versions and secret-free LiveKit arguments.

Evidence must never contain passwords, cookies, API keys, API secrets, invite tokens, access tickets or media join tokens. Database variables are passed only to backend integration tests; LiveKit variables are passed only to the media CLI. The scanner sanitizes exact configured secrets, JWTs and token-like base64url values. Detection fails the run even after the value is removed.

The word **record** means reproducible test evidence and telemetry. It does not enable or imply recording meeting audio, video, screen share or chat. Meeting recording remains disabled until consent, retention, permissions and storage are separately approved.

## Profiles and outcomes

- `ControlPlane`: repeats the two exact backend scale scenarios and frontend collaboration tests, then runs frontend lint/build.
- `MediaSmoke`: conservative same-host synthetic LiveKit media exercise, repeated once per requested loop.
- `Local150`: 150 synthetic connections per loop: 4 video publishers, 6 audio publishers and 140 subscribers. Resource preflight and continuous CPU/memory/Docker gates apply.
- `All`: requests all three component profiles. A video-heavy/WAN test still requires separate Linux load generators and real browsers.

Component status is always one of `Passed`, `Failed` or `Blocked`. Aggregate `runOutcome` is one of:

- `ComponentPassed`: every requested local component passed;
- `Partial`: some requested evidence passed but coverage or a gate remains incomplete;
- `Blocked`: no requested component could run;
- `Failed`: a component, evidence scan or harness operation failed.

`finalQualified` is independent from `runOutcome`. A local component run may be `ComponentPassed` while `finalQualified` remains `false`. The runner prints `Final qualification: Passed` only when every required automated and human gate is explicitly satisfied; currently browser QoE and external/human gates are recorded as `NotMeasured` or `NotVerified`, never silently passed.

Exit codes are `0` for `ComponentPassed`, `1` for `Failed`, and `2` for `Partial` or `Blocked`.

## Required environment

Backend tests read the existing PostgreSQL process variables. Media profiles read:

```text
LIVEKIT_URL
LIVEKIT_API_KEY
LIVEKIT_API_SECRET
```

Credentials are placed in child-process environment only, never command arguments or reports. Use the pinned official `lk` CLI version detected by the runner; do not commit the executable.
The resolver first checks `.local-host/tools/livekit-cli/2.18.6/verified/lk.exe`, then the legacy pinned location, and finally `lk.exe` on `PATH`.

## Threshold enforcement

`thresholds.json` distinguishes:

- CLI-measurable track delivery, reported latency and track-drop metrics, which are parsed and enforced when present;
- browser/WebRTC QoE metrics such as join latency, RTT, jitter and freeze ratio, which the CLI cannot supply and are therefore `NotMeasured`;
- hard process timeout plus preflight and continuous host resource abort limits.

Control-plane success is fail-fast and requires both exact TRX tests to exist and pass in every requested loop. The covered API operations assert their expected HTTP status, so any unexpected rate limit or server error fails the functional test. However, this runner does not calculate aggregate request success/server-error/rate-limit rates or request-level p95/p99 latency. Those numeric `controlPlane` thresholds remain acceptance targets for a later deployed HTTP load test and must not be reported as measured by this harness. Media gates likewise require both the LiveKit process and its parsed measurable metrics to pass in every requested loop; an early stop cannot satisfy the loop gate.

The pinned LiveKit CLI `2.18.6` summary currently exposes delivered/expected track counts and packet loss, but not a per-track latency value. Consequently `trackLatencyP95Milliseconds` is recorded as `NotMeasured`, and the measured-metrics component remains `Blocked` under the current fail-closed thresholds. This is not evidence of acceptable latency; a browser/WebRTC telemetry run is still required.
Any non-zero aggregate client error in the CLI subscriber summary fails the media process even if the CLI exits with code `0`. Packet-loss percentage is accepted only from the CLI aggregate row; the runner never divides lost-packet counts by track counts because those units are incompatible.

## Final enterprise acceptance

A final acceptance run requires at least:

- three consecutive successful control-plane loops;
- all required component profiles;
- real devices and supported browsers on LAN and external networks;
- UDP, TCP fallback and TURN paths;
- measured browser QoE thresholds;
- human task-success and accessibility review;
- no open P0/P1 defect.

Synthetic load on one workstation cannot truthfully certify “150 human users” or a UX score above 9/10.
