# VillaPCG v4 Design Notes

## Goal

VillaPCG v4 extends the PCG map generator so the generated villa layout also drives enemy placement. The intent is to reduce manual enemy setup and make the patrol/stand enemy layout explainable as PCG rules.

## Map PCG Rules Used By Enemy Placement

- Rooms use two map layout zones for agent PCG: `Casual` and `Restricted`.
- `Casual` rooms are normal traversal/social spaces.
- `Restricted` rooms are controlled spaces where enemy agents and restricted-floor visuals are allowed.
- Generated rooms also create invisible area marker colliders on Unity layers named `CasualArea` and `RestrictedArea`.
- The visible floor stays on the walkable navigation layer; the area marker layer is used by perception/gameplay checks.
- Enemy agents may only be generated in rooms whose layout zone is `Restricted`.
- Restricted floors use a red restricted color layer so these agent-eligible regions are visible in runtime/editor inspection.
- The room graph is built from generated room connections, so each room has a graph distance from the player spawn room.
- Goal rooms, final target rooms, exit rooms, and spawn rooms are known outputs of the map generator.
- Room size is available from the generated room bounds.

These map outputs are reused as enemy-placement inputs instead of placing enemies by fixed world coordinates.

## Enemy Rule: RoomThreatScore

Patrol candidates are sorted by a score derived from generated map data:

- Rooms outside the `Restricted` layout zone are rejected.
- Restricted rooms that contain goals, targets, or strong tactical coverage receive higher priority.
- Rooms deeper in the room graph from the player spawn receive higher priority.
- Larger rooms receive higher priority because they can support patrol movement.
- Primary goal and final target rooms receive extra priority.
- Rooms near the player spawn are penalized by `enemySpawnExclusionRadius`.

The highest scoring restricted rooms receive `patrolEnemyTest_PCG_XX` agents. Each patrol enemy gets a generated route named `PCG_Enemy_Patrol_XX`. Patrol waypoints are kept away from walls and corners to reduce wall-sticking cases.

## Enemy Rule: RoomPairPatrolScore

Additional patrol agents can use a two-room shuttle route alongside the normal single-room patrol loops:

- Candidate routes are built from adjacent `Restricted` rooms.
- One anchor is placed inside each room near the shared connection.
- The route loops between the two anchors.
- These anchors use a longer wait time than normal patrol waypoints.
- The generated route name uses `PCG_Enemy_PairPatrol_XX`.

## Enemy Rule: DoorGuardScore

Standing guards are generated from room connections:

- The `Restricted` side of a connection is selected as the guarded room.
- Connections whose guarded room is outside the `Restricted` layout zone are rejected.
- Doors that separate `Casual` and `Restricted` layout zones are prioritized.
- Doors near the primary goal receive extra priority.
- Door guard positions are pushed inside the guarded room by `standingGuardDoorOffset`.
- If the guarded room already contains a single-room looping patrol enemy, standing candidates are placed at the room's four inset corners instead of behind or near the patrol route.
- Positions too close to each other are filtered.

The highest scoring restricted door candidates receive `standEnemyTest_PCG_XX` agents. Each standing guard gets a generated single-point route named `PCG_Enemy_Stand_XX`.

## Reproducibility

Enemy candidate ordering uses deterministic tie-breakers after score comparison. Room candidates fall back to room name and room center, and door guard candidates fall back to room name and guard position. This keeps layouts stable for the same seed and generated map, even when two candidates receive the same score.

## Runtime Integration

- Generated routes are inserted into `RouteManager.allRoutes`.
- `RouteManager.RebuildRouteDictionary()` makes newly generated routes available to `AgentBrain`.
- Patrol enemies use `AgentDecision.PATROL`.
- Standing enemies use `AgentDecision.LONGREST`.
- `AgentNavigator` references are assigned to the generated `GridMap3D` and `WaypointGraph3D`.
- `SensorySystem` probes the `CasualArea` layer under the player or audio stimulus. Players seen in `CasualArea` are ignored for combat, and non-combat audio in `CasualArea` does not start investigation.

## Debug Evidence

After generation, `VillaPCG_v4.lastMapRuleSummary` records:

- seed, final villa style, and complexity
- room count, connection count, and room graph node count
- layout-zone distribution
- player spawn, primary goal, secondary goal, exit, and final target rooms
- reachability results for the generated level flow

After generation, `VillaPCG_v4.lastEnemyPlacementSummary` records:

- enemy name
- generated route name
- selected room
- generated position
- rule reason, layout zone, and score

Scene gizmos also draw red spheres at generated enemy spawn positions when waypoint gizmos are visible.

Generated route waypoint object names include the selected room token, for example `PCG_Enemy_Patrol_01_Target_Room_WP_01`, so the hierarchy also shows which PCG room rule produced the route.

The Inspector button `Copy PCG Report` combines `lastMapRuleSummary` and `lastEnemyPlacementSummary` into one clipboard-ready report for course writeups or live PCG demonstrations.

## Validation Workflow

VillaPCG v4 includes two editor validation paths:

- Inspector: select the `PCG_Villa_v4` object and press `Generate + Validate Enemy PCG`.
- Inspector: press `Generate + Validate + Save Scene` to rebuild the generated enemy hierarchy and save the scene only after validation passes.
- Inspector: press `Copy PCG Report` after generation to copy the current map-rule and enemy-layout evidence.
- Menu: run `Tools > VillaPCG v4 > Validate UltimatePCG_v2 Enemy PCG`.
- Menu: run `Tools > VillaPCG v4 > Generate Validate Save UltimatePCG_v2 Enemy PCG` to open `UltimatePCG_v2`, regenerate the enemy layout, validate it, and save the scene.

The validation checks:

- generated enemy and route roots exist
- generated patrol and standing enemy objects exist
- generated routes exist in `RouteManager`
- `lastMapRuleSummary` contains map rule and reachability data
- every generated enemy has `Agent`, `AgentBrain`, and `AgentNavigator`
- patrol enemies use `AgentDecision.PATROL`
- standing enemies use `AgentDecision.LONGREST`
- every enemy has a route name that resolves through `RouteManager`
- route waypoints are valid
- route waypoint names include the selected room token
- generated enemy navigator references match `VillaPCG_v4.gridMap` and `VillaPCG_v4.waypointGraph`
- the placement summary includes PCG rule names such as `RoomThreatScore`, `RoomPairPatrolScore`, or `DoorGuardScore`
- generated agent rooms are in the `Restricted` layout zone

If validation reports route waypoint names without room tokens, the scene contains stale generated routes from an older v4 iteration. Run `Generate + Validate Enemy PCG` once to rebuild those generated route objects with the current naming rule.

For automation, Unity can run:

```bash
Unity -batchmode -projectPath <project-path> -executeMethod VillaPCG_v4ValidationRunner.ValidateUltimatePcgV2EnemyPcg
```

This exits with code `0` on pass and `1` on failure when Unity licensing is available.

To regenerate and save `UltimatePCG_v2` from automation:

```bash
Unity -batchmode -projectPath <project-path> -executeMethod VillaPCG_v4ValidationRunner.GenerateValidateSaveUltimatePcgV2EnemyPcg
```
