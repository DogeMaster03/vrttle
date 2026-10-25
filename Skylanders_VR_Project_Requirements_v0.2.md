**REVISED PROJECT SPECIFICATION**

**Skylanders VR**

Virtual-room front end for RPCS3 • Product requirements document

**Version  **0.2  |  October 21, 2026  |  Draft, not requirements-frozen

**Confirmed platform  **Godot 4 desktop VR, Meta Quest over PC Link / Air Link, Windows host + RPCS3

**User-approved scope  **All six main Skylanders titles; placeholder figures first; two gameplay-control modes; launch and attach; headset audio in MVP

**Scope note  **Unanswered implementation choices are tracked as OPEN, not silently resolved.

# **1. Product vision**

Build an immersive VR room where the player sees a live capture of a PlayStation 3 Skylanders game running in RPCS3 on a wall-mounted virtual display, and interacts with a virtual Portal of Power. Virtual figure placement maps to local .sky files through the RPCS3 Skylanders Portal IPC server. The application wraps unmodified emulation; it does not render the game world stereoscopically.

Primary user journey: launch the VR app → choose to launch RPCS3 or attach to an existing running session → connect the Meta Quest using Link or Air Link → see the live game and hear it in the headset → pick up a placeholder figure → place it on the virtual portal → RPCS3 acknowledges its slot → continue playing through a gamepad or emulated gamepad input from Quest controllers.

# **2. Decisions incorporated in this revision**

| **Decision** | **Binding requirement** |
| - | - |
| D-01: Headset | Meta Quest operating as a PC VR headset via wired Quest Link or wireless Air Link. Standalone Quest APK is out of scope. |
| D-02: Engine | Godot 4 with OpenXR; select and lock an exact stable Godot release after prototype validation. |
| D-03: Figure visuals | Functional placeholder meshes/cards and readable labels at MVP; full collectible 3D models are later optional content. |
| D-04: Titles | Support the six main PS3 Skylanders releases at full-release sign-off; track title-level compatibility throughout development. |
| D-05: Controls | Two selectable in-game input modes: physical controller in RPCS3 and Quest VR controllers mapped to gamepad actions. |
| D-06: RPCS3 startup | Both launch-and-manage and attach-to-existing-session workflows must be supported. |
| D-07: Audio | Game audio audible in the Meta Quest headset is a MUST for MVP, in both Link and Air Link modes. |


# **3. Scope and boundaries**

## **3.1 Included**

- Godot room scene, virtual cinema screen, Portal of Power, figure shelf/library, interaction cues, and comfort settings.

- RPCS3 launch/attach manager, portal IPC adapter and state reconciliation, process/window selection, and diagnostics.

- Windows game-window capture to a Godot texture, wired/wireless PC VR compatibility, and headset game-audio routing.

- Physical gamepad play plus an alternative Quest-controller-to-PS3-gamepad mapping with a user-selectable mode.

- Compatibility validation for Spyro’s Adventure, Giants, SWAP Force, Trap Team, SuperChargers, and Imaginators.

## **3.2 Excluded from the initial build**

- Native Android/standalone Quest build, stereoscopic conversion of PS3 game rendering, extracted commercial character models/assets, game ROM/firmware distribution, and cloud syncing.

- Multiplayer-focused interaction design, figure save editing, full portal light/audio hardware emulation, and passthrough mixed reality unless subsequently approved.

## **3.3 Explicit baseline assumptions**

One local Windows gaming PC runs Godot and RPCS3. One Meta Quest headset is connected by Link or Air Link. The app uses user-provided, legally obtained game and .sky assets. Windows 11 is recommended for the first integration spike; Windows 10 support remains OPEN. A known-good RPCS3 build with portal IPC enabled is a dependency.

# **4. High-level architecture**

Meta Quest (Link / Air Link) ⇄ OpenXR runtime ⇄ Godot VR front end

Godot → \[RPCS3 launcher / attach selector\] → RPCS3 process & gameplay window

Godot → \[Skylander IPC client: TCP\] → RPCS3 emulated Portal of Power

RPCS3 gameplay HWND → \[Windows Graphics Capture bridge: GDExtension/native helper\] → Godot game-screen texture

RPCS3 audio → \[selected Windows headset playback endpoint OR approved capture/replay bridge\] → Quest headset

Quest XR input → \[configurable gamepad emulation adapter\] → RPCS3 input; OR physical controller → RPCS3 input

Godot GDScript handles UI, figure state, room geometry and orchestration. The capture/texture bridge should be implemented with a maintained GDExtension or lightweight native helper, favoring a low-copy texture path if proven safe. Do not assume Godot’s standard desktop screenshot functions can directly capture the foreign RPCS3 window.

# **5. Functional requirements**

| **ID** | **Priority** | **Requirement / observable behavior** |
| - | - | - |
| XR-01 | MUST | Initialize OpenXR through the active Quest-compatible PC runtime; render a stable seated-friendly room. Diagnose runtime or headset loss. |
| XR-02 | MUST | Allow VR-controller grab, release, portal placement, cancellation, haptic/visual feedback where available, and recentering. |
| XR-03 | SHOULD | Provide adjustable virtual-screen size, position, seating height and readable menu scale; no required free locomotion. |
| LIB-01 | MUST | Index a user-selected folder of local .sky files; show unique, labeled placeholders and preserve correct absolute file mapping. |
| LIB-02 | MUST | Show empty, pending, loaded and failed visual states; indicate occupied slots and unknown externally loaded figures. |
| PORT-01 | MUST | Implement load, remove, status and clear via RPCS3 portal IPC, respecting eight logical slots and acknowledgements. |
| PORT-02 | MUST | Poll and reconcile emulator state; avoid falsely assuming a figure remains loaded after a disconnect or external edit. |
| CAP-01 | MUST | Attach to the gameplay window and display correctly proportioned live frames in the room; recover from window changes. |
| AUD-01 | MUST | Route RPCS3 game audio to Quest headset for wired Link and Air Link, without requiring manually unplugging another sound device. |
| CTRL-01 | MUST | Support physical controller routed to RPCS3 for gameplay while VR controllers operate room/portal interactions. |
| CTRL-02 | MUST | Offer Quest-controller gameplay mode, translate axes/buttons to a PS3-compatible virtual input route, and provide remapping. |
| CTRL-03 | MUST | Keep portal interactions distinguishable from gamepad actions with a mode toggle, focus policy and visible active-mode state. |
| OPS-01 | MUST | Allow launch configured RPCS3 executable + game using supported invocation, or attach to an already-running instance. |
| OPS-02 | MUST | Report IPC connection, selected process/window, capture, audio endpoint and controller route; offer actionable recovery. |
| GAME-01 | MUST | Provide an end-to-end compatibility test and tracked outcome for each of the six target games. |
| GAME-02 | MUST | Represent game-specific requirements (swap halves, traps, vehicles, creation crystals) explicitly in test coverage. |


# **6. RPCS3 portal IPC contract**

Upstream RPCS3 PR \#18960 was merged October 10, 2026. Windows IPC is a loopback TCP service, normally 127.0.0.1:28013, disabled by default and configurable under Manage → Network Services → Skylanders Portal IPC. Verify the installed RPCS3 build and settings; neither the endpoint nor port must be treated as immutable.

| **Command** | **Meaning / response expectations** |
| - | - |
| load -1 C:\\Figures\\Spyro.sky | Request first free slot; success response is ok \<slot\>; -1 means auto-select. |
| load 2 C:\\Figures\\Spyro.sky | Request logical slot 2. Validate returned slot rather than assuming slot selection always succeeds. |
| remove 2 | Remove from logical slot 2; success ok, failure error \<reason\>. |
| status | Return 8 slot lines such as slot0 loaded \<id\> \<variant\> or slot0 empty, terminated by ok. |
| clear | Clear all portal slots; use only with explicit user action or documented shutdown preference. |


Commands are newline-terminated request/response operations; do not assume pushed events or persistent subscriptions. The client shall handle timeouts, partial reads, IPC unavailable during emulator startup, invalid paths, occupied/incorrect slots, and reconnect. After an ambiguous timeout, reconcile with status rather than replaying a possibly successful load blindly. Figure save files may be written by RPCS3; the VR app shall not alter file contents or claim data integrity without tests.

# **7. Game capture, headset audio and input**

## **7.1 Window capture to Godot**

Identify the gameplay HWND belonging to RPCS3; exclude RPCS3’s library and settings windows. Prototype Windows.Graphics.Capture with a Godot GDExtension or helper process. A published third-party window-capture GDExtension exists, but suitability, license, Godot-version support and performance require evaluation. Preserve aspect ratio; present lost capture as a recoverable paused state. Test resize, display scale, alt-tab, minimize, borderless/fullscreen and renderer changes before locking implementation.

## **7.2 Quest audio (MVP gate)**

Provide a setup wizard or automatic configuration path that causes RPCS3 audio to play in the Meta Quest headset output over both Link and Air Link. First investigate selecting the Quest virtual audio endpoint through Windows / RPCS3; investigate per-process audio capture only if endpoint routing is inadequate. Avoid capturing unrelated application audio, duplicate playback and perceptible audio drift. Test after Link/Air Link reconnects, audio-device changes and emulator restarts.

## **7.3 Dual gameplay control modes**

Mode A — Physical gamepad: the controller is connected/configured in RPCS3; Quest controllers remain available for portal and VR UI. Mode B — VR gameplay: OpenXR action bindings feed a dedicated input-translation layer that exposes controls to RPCS3 as an emulated compatible gamepad; map thumbsticks, face buttons, shoulder/trigger functions, directional pad, pause and system/menu behaviors. User can switch modes deliberately; do not require both to inject game actions simultaneously. Exact virtual-device driver, support for motion-specific game gestures and button layout remain OPEN until proven.

# **8. Six-game target and compatibility acceptance**

All six games are in final project scope, not automatically guaranteed by the RPCS3 portal IPC API. Each release gate requires actual playtesting with that title and supported figure types. Game compatibility and specialty figure recognition must be verified rather than inferred from generic load/remove success.

| **Title (PS3)** | **Integration scenarios to validate** |
| - | - |
| Spyro’s Adventure (2011) | Basic figure placement/removal, readable gameplay, physical / Quest-input modes, headset audio. |
| Giants (2012) | Giants and ordinary figures, portal slot transitions, save persistence, capture/audio. |
| SWAP Force (2013) | Both swapped components/combinations; correct representation and recognition where emulator supports it. |
| Trap Team (2014) | Trap slot / villain interactions, relevant game audio behavior and required portal emulation capability. |
| SuperChargers (2015) | Vehicle/driver figure flows and transitions; input bindings used in racing sections. |
| Imaginators (2016) | Creation-crystal / Sensei interactions and any special save behaviors. |


Support strategy: demonstrate a complete vertical slice in one pilot title (OPEN which title); then expand through the remaining five. Full release is blocked if a core game-specific portal mechanic required for progression cannot be represented. That issue must be tracked as a dependency on RPCS3 emulation or a separately approved adapter extension.

# **9. Performance, reliability and security targets**

| **Area** | **Provisional acceptance threshold / test** |
| - | - |
| VR rendering | Meet the user-selected Quest headset refresh mode (e.g., 72 or 90 Hz if available) without persistent missed frames in representative gameplay; profile per PC. |
| Capture | Source at native resolution/aspect; no frozen image under sustained normal playback; log capture-to-screen delay and dropped frames. |
| Portal responsiveness | Proposed ≤ 500 ms healthy-local-action feedback at P95 including IPC acknowledgement, subject to feasibility. |
| Audio | Audible in headset in both Link and Air Link; no repeated dropouts in a 30-minute session; no unrelated app audio captured. |
| Reliability | Recover from IPC restart, missing window, VR runtime interruption and audio endpoint changes without corrupting figure saves. |
| Access control | Default IPC to loopback; never silently scan or rewrite user .sky folders; allow only explicit user-selected paths. |
| Compatibility | Six named PS3 titles each pass core gameplay, basic figure interaction, special-mechanic tests, and both control modes. |


Numbers are proposed gates, not guaranteed product claims. Final latency, operating-system floor, headset refresh profile, GPU/CPU hardware budget and minimum game frame rate remain OPEN for acceptance sign-off.

# **10. Acceptance test checklist**

| **Test** | **Pass criterion** |
| - | - |
| AT-01 • OpenXR | Connect a Quest by USB Link and by Air Link in separate tests; enter the same room and complete controller interaction. |
| AT-02 • Launch + attach | Start a configured RPCS3 game from the app, and attach to an existing game without launching a duplicate process. |
| AT-03 • IPC load/remove | Load a test .sky figure, verify returned slot with status, remove it, and verify the slot is empty. |
| AT-04 • External reconcile | A figure loaded or removed in RPCS3 outside Godot is reflected in the room after status polling. |
| AT-05 • Capture recovery | Correct video appears; switching/resizing/restarting the game window never crashes the VR application. |
| AT-06 • Headset audio | Game sound routes to the headset in wired and wireless modes, including after endpoint reconnect. |
| AT-07 • Physical gamepad | Complete a gameplay scene using a physical gamepad while the VR hand(s) can operate the portal. |
| AT-08 • Quest gameplay | Complete the same scene using Quest controls mapped to PS3 actions; switch modes without stuck inputs. |
| AT-09 • Failure handling | IPC off, bad .sky files, no slots, missing audio output, missing capture and disconnected headset show recoverable errors. |
| AT-10 • Six-game matrix | Document evidence and pass/fail for each game’s main figure and special mechanic tests, plus control and audio paths. |


# **11. Delivery plan and release gates**

| **Phase** | **Deliverables / exit condition** |
| - | - |
| A — Feasibility | Godot OpenXR room in Link/Air Link; IPC test client; gameplay capture on Godot plane; audible headset audio; prototype Quest-input gamepad mapping. |
| B — MVP vertical slice | Launch + attach; functioning placeholder shelf and portal; reliable video/audio; both control modes; complete play session for one pilot title. |
| C — Compatibility | Test all six PS3 games; implement/track game-specific interaction needs; replace blocker assumptions with measured compatibility evidence. |
| D — Polishing | Menus, diagnostic wizard, room comfort, rebinding, packaging, save/backups, optional richer figure models and effects. |


The full release requires Phase C acceptance. Phase B is deliberately a vertical-slice MVP; it does not imply the other five games are already validated. MVP must include headset audio and both launch/attach and gameplay-control modes as confirmed above.

# **12. Open decisions — not yet approved**

| **ID** | **Question** | **Current disposition** |
| - | - | - |
| O-01 | Which Meta Quest model(s) and exact Windows version(s)? | OPEN; Quest PC VR over Link/Air Link is fixed. |
| O-02 | Which specific stable Godot 4 version and OpenXR runtime are minimum supported? | OPEN; prototype with current stable + Mobile renderer. |
| O-03 | Which one Skylanders title should be the initial MVP vertical slice? | OPEN; all six remain full-project target. |
| O-04 | Which capture bridge: off-the-shelf GDExtension, custom GDExtension, or helper process? | OPEN; decide after latency/performance spike. |
| O-05 | Which virtual gamepad emulation solution and Quest button mapping? | OPEN; must be tested with actual RPCS3. |
| O-06 | How will Trap Team traps, SWAP Force halves, vehicles and creation crystals be represented in the portal UI? | OPEN; per-game design and emulator capability test. |
| O-07 | What should happen to the RPCS3 process and portal slots when Godot closes? | OPEN; default: do not terminate or clear without consent. |
| O-08 | How should the user select games, figure folders and audio endpoints? | OPEN; provide accessible setup/diagnostics in MVP. |
| O-09 | What PC performance floor, target headset refresh rate and packaging/distribution model? | OPEN; derive from real hardware profiling. |
| O-10 | One player only, or local co-op in the release? | OPEN; first integration assumes single VR user. |
| O-11 | Headset audio via direct output routing or process-specific capture bridge? | OPEN; mandatory audio result is fixed, implementation is not. |
| O-12 | Does the user expect full emulation of portal LEDs and trap-speaker effects? | OPEN; not claimed by current text IPC. |


# **13. Risk register**

| **Risk** | **Mitigation** |
| - | - |
| Quest PC VR + RPCS3 compete for GPU time | Profile early using a real headset with both processes under demanding scenes; allow quality presets. |
| Capture frames difficult to share efficiently with Godot | Prototype texture copying/interop early and measure frame delay and format compatibility. |
| Quest controller mapping does not reproduce every PS3 input | Inventory controls per title; design remapping and a documented physical-controller fallback. |
| IPC covers portal figures but not every title’s special features | Test special interactions before promising full six-game compatibility; escalate unsupported paths. |
| Audio endpoint changes between wired and wireless VR | Treat reconnection as a first-class test; surface current output device and recover automatically if possible. |
| Figure save data is modified during gameplay | Use RPCS3 as file authority; no concurrent writes; back up test figures and check integrity. |
| User-owned game/model asset rights and packaging | Distribute no commercial games, firmware, figures or ripped assets; use neutral placeholders. |


# **14. Engineering source notes**

RPCS3: Skylanders Portal IPC PR \#18960 (merged October 10, 2026): https://github.com/RPCS3/rpcs3/pull/18960

RPCS3: Current portal IPC implementation: https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/Io/SkylanderPortalIPC.cpp

Godot: Setting up XR (OpenXR): https://docs.godotengine.org/en/stable/tutorials/xr/setting\_up\_xr.html

Godot XR Tools: Grab / pickable object guidance: https://godotvr.github.io/godot-xr-tools/docs/pickable/

Prototype window capture extension (evaluate, not adopted): https://github.com/punikonta/godot-gdextension-window-capture

Microsoft: Window capture HWND interop: https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow

Activision: Skylanders toy / game compatibility: https://support.activision.com/no/skylanders-superchargers/articles/skylanders-toy-compatibility-faq

Verification note: Source links describe candidate APIs and observed upstream behavior. Acceptance requires verifying the exact installed versions, user hardware and all six games.
