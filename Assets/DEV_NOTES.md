# Development Notes

These notes collect small engineering decisions that make ShootGuy faster to build and easier to maintain. Each section includes the code change and the programming habit behind it.

## Level Navigation Interface

`LevelExit` now talks to `ILevelNavigator` instead of hard-coding every PCG version into its interaction logic. Any future level manager can become compatible by implementing:

```csharp
public interface ILevelNavigator
{
    void GoToLevel(int levelNumber);
}
```

This keeps the exit trigger stable while PCG classes continue to evolve.

Practical rule: when one script only needs one behavior from another script, depend on that behavior through a small interface instead of depending on the full concrete class.

## Shared Player Lookup

`PlayerLocator` centralizes the fallback rules for finding the player: tag first, `ThirdPersonController` second, and a non-AI `CharacterController` last. This avoids copying the same scene-search code into every system that needs the player.

Practical rule: if two or more scripts need the same "how do I find X?" logic, put that lookup behind one small helper so future prefab, tag, or component changes happen in one place.

## Inspector Debug Buttons

`PathPlanningTester` has Inspector buttons for assigning the graph, finding a path, rebuilding the graph before finding, and clearing the current debug path. It also shows the last result message, path node count, path cost, and expanded node count in the Inspector. This makes pathfinding iteration faster than repeatedly opening component context menus and checking Console output.

Practical rule: when a debug action is used often during scene tuning, expose it as a small editor button near the data it operates on. Keep the runtime method public and simple, then wrap the editor-only UI in `#if UNITY_EDITOR`.

Second practical rule: a debug tool should leave useful state behind after it runs. Console logs are easy to miss; fields like last result, cost, and expanded node count make repeated tuning much easier to compare.

## Unity Git Hygiene

Unity can create `Assets/_Recovery` scenes when the editor recovers unsaved work. Those files are useful locally, but they are usually not intentional source assets, so new recovery files are ignored by `.gitignore`.

Practical rule: commit real scenes, prefabs, scripts, materials, and their `.meta` files; ignore editor caches, logs, temporary folders, and auto-recovery output unless you deliberately turn one into a real scene.

## Toggle Noisy Debug Logs

`PlayerLedgeClimb`, `PlayerAimController`, `BulletPool`, `SimpleProjectile`, `Agent`, `AgentBrain`, `AgentNavigator`, `BehaviorManager`, and `Player` keep high-frequency interaction logs behind a `showDebugLogs` toggle. `AgentBrain`, `AgentNavigator`, and `BehaviorManager` reuse `Agent.showDebugLogs`, so one switch controls AI lifecycle, investigation, patrol, navigation, steering, and combat phase details. This keeps normal playtesting readable while still allowing detailed ledge, aiming, pool, projectile, AI lifecycle, investigation, patrol, navigation, steering, combat, and equipment debugging when needed.

Practical rule: keep high-frequency debug logs behind a bool or a central logging helper. Remove stale logs, but preserve useful diagnostics when they help tune movement, AI, or procedural generation.

## Extract Repeated Intent

`Player` now routes keyboard equipment shortcuts through `Equip(EquipmentType equipment)`. The input branches decide which equipment is requested, while `Equip` owns the state change and optional debug message.

Practical rule: when several input branches do the same kind of work with different values, extract the shared intent into one method. This reduces copy-paste, gives other systems a clean API to call, and makes future rules such as cooldowns, validation, or UI updates easier to add in one place.

## Avoid State Shadowing

`SensorySystem.FieldOfViewCheck()` uses a local `sawPlayer` variable and then writes the result back to the `canSeePlayer` field. This keeps the Inspector state, Gizmos, hearing logic, and vision logic aligned. The previous local variable name matched the field name, which made the field look meaningful while it was not being updated.

Practical rule: avoid giving local variables the same name as important fields. When a field is part of runtime state or debugging telemetry, update it deliberately and keep temporary calculation variables clearly named.

## Cache Repeated Component Lookups

`PlayerAimController` caches its Cinemachine camera components instead of calling `GetComponent<CinemachineVirtualCamera>()` every frame. `AgentNavigator` also uses `TryGetComponent` when reading target velocity, so it does not query the same target component multiple times in one update. `SimpleProjectile` and `Coin` cache the obstacle layer id in `Awake()` instead of resolving `"Obstacle"` during every trigger hit. The aim controller still retries if a reference is missing, but normal play no longer pays that lookup cost in `Update()`.

Practical rule: cache stable component references and fixed ids during initialization, then let `Update()` and collision callbacks focus on state changes. Use `TryGetComponent` when you only need a component for the current operation, and keep a fallback retry only when scene references may be assigned late.

## Avoid Scene-Wide Searches In Hot Paths

`Agent` maintains an `ActiveAgents` registry with `OnEnable()` and `OnDisable()`. The registry is also cleared with `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)`, so it starts clean even when Unity enters Play Mode with domain reload disabled. `AgentBrain.CalculateDodgePoint()` now reads that registry instead of calling `FindObjectsByType<Agent>()` every time an enemy evaluates cover crowding. This keeps tactical dodge scoring from repeatedly scanning the whole scene as enemy counts grow.

Practical rule: scene-wide searches are fine for setup tools and occasional fallback paths, but avoid them inside repeated gameplay decisions. If a system repeatedly needs "all active X", maintain that list at the lifecycle boundary where objects appear and disappear. For static caches in Unity, add an explicit play-session reset so Editor settings do not leave stale state behind.

## Guard Required References

`PlayerAimController` validates its basic references before running aim logic, and separately validates firing systems before spawning bullets or coins. `BulletPool` guards missing pooled prefabs before instantiating. `SimpleProjectile` safely disables itself if it cannot return to a missing `BulletPool`, and `SimpleProjectile` / `Coin` also guard audio stimulus broadcasts when `StimulusManager` is missing. Missing setup now produces one clear warning instead of a repeated null-reference failure.

Practical rule: guard required references at the boundary of an action. Split checks by capability, so missing firing services do not unnecessarily disable unrelated camera or aim behavior. Service scripts should fail clearly at the creation boundary, and callers should handle a missing returned object safely.

`Agent` declares its core AI component bundle with `[RequireComponent]`: `AgentLocomotion`, `AgentNavigator`, `AgentBrain`, and `SensorySystem`. Future enemy prefabs created by adding `Agent` are less likely to miss the movement, navigation, brain, or perception scripts that the AI hub expects.

Practical rule: use `[RequireComponent]` for structural prefab contracts, then keep runtime guards for partially assembled legacy objects and generated content. Attributes prevent common setup mistakes; guards keep the game from crashing when data is still imperfect.

`Agent` also checks for its animation dependencies before syncing animation state each frame, and it handles a missing `AgentBrain` during death cleanup with one warning instead of a null-reference crash. This makes temporary PCG or test prefabs safer while they are still being assembled.

Practical rule: per-frame code should quickly prove that its required dependencies exist, then return early if they do not. Use a one-time warning for setup problems so the Console points at the missing dependency without flooding every frame.

`AgentBrain` separates core AI dependencies from combat dependencies. Missing `AgentNavigator` or `SensorySystem` disables the brain update, while missing `shootPoint`, `Animator`, or `BulletPool` only blocks combat setup or shooting with one clear warning. This makes partially assembled PCG enemies easier to diagnose.

Practical rule: validate dependencies by capability, not by script. A behavior tree, state machine, or controller often has independent capabilities; failing one capability should not always disable every other capability.

`Agent.velocity` now returns `Vector3.zero` with one warning when `AgentLocomotion` is missing. Steering, pursuit, and avoidance systems can keep reading the property safely while the prefab setup issue remains visible in the Console.

Practical rule: if a public property is used as shared state by many systems, keep its contract stable. Returning a safe fallback at the property boundary is usually better than forcing every caller to repeat the same null check.

`BaseBehaviorManager` now guards steering computation when `AgentNavigator` or `AgentLocomotion` is missing. All steering strategies inherit the same early return, so temporary prefabs fail with one useful warning and zero steering instead of separate null-reference failures in each manager.

Practical rule: when several subclasses share the same dependency contract, validate it once in the base class. This keeps new strategy implementations focused on their algorithm instead of repeating setup checks.

`AgentNavigator` now validates its per-frame navigation dependencies before doing path or steering work, and it wraps braking in `ApplyBrake(float dt, bool smoothBrake)`. Waiting keeps its smoother brake, while arrive keeps its stronger stop, and both paths avoid division by zero when `deltaTime` is not usable.

Practical rule: when extracting a helper from gameplay code, preserve the original behavior differences explicitly. A helper should remove duplicated mechanics without silently changing movement feel.

## Singleton Initialization

`BulletPool` exits `Awake()` immediately after destroying a duplicate instance, and clears `Instance` in `OnDestroy()` when the active pool is destroyed. That prevents duplicate service objects from initializing pools or spawning children after they have already been rejected, and avoids stale singleton references after scene teardown.

Practical rule: when a singleton rejects a duplicate, return immediately after `Destroy`. Do not let rejected instances continue initialization work, and clear static instance references when the owning object is destroyed.

## Temporary Object Lifecycle

`AgentBrain` creates one `DummyTarget_*` GameObject for navigation requests that target a position instead of a real scene object. The dummy target is parented under its owning agent and destroyed in `OnDestroy()`, so repeated Play Mode runs, PCG regeneration, or enemy teardown do not leave orphan helper objects in the scene.

Practical rule: every runtime-created helper object needs a clear owner and a cleanup point. Parent it near the system that owns it, access it through a small helper when possible, and destroy it when the owner is destroyed.
