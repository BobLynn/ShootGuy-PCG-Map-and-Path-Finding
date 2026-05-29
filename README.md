# Shoot Guy

Final Project for AI for Games (Spring 2026)

A Hitman-style stealth sandbox featuring:

- Procedural Content Generation (PCG)
- Custom Pathfinding System
- Grid Graph Navigation
- Waypoint Graph Navigation
- Steering Behaviors
- FSM / Behavior Tree AI
- Guard and Target AI Systems

---

# Unity Version

This project must be opened with:

Unity 6000.4.5f1

Using other Unity versions may cause:

- URP incompatibility
- Package mismatch
- Missing references
- Serialization issues

---

# Render Pipeline

Universal Render Pipeline (URP)

---

# Required Packages

Installed through Package Manager:

- AI Navigation
- Cinemachine
- Input System

---

# External Assets

The following third-party assets are NOT included in this repository:

## Apartment Kit

Unity Asset Store

https://assetstore.unity.com/packages/3d/environments/apartment-kit-124055

Import manually before opening the main scene.

---

# Main Systems

## PCG System

File:

Assets/Scripts/PCG/VillaPCG_v2.cs

Features:

- Runtime map generation
- Public / Restricted areas
- Obstacle generation
- Room generation
- Navigation evaluation

---

## Pathfinding

Grid-based navigation

Files:

- GridMap.cs
- GridMap3D.cs

Waypoint-based navigation

Files:

- WaypointGraph.cs
- WaypointGraph3D.cs

Algorithms:

- A*
- Dijkstra

---

## Agent AI

Player Robot

Features:

- Movement
- Ledge Climbing
- Navigation Testing

Guard AI

Features:

- Patrol
- Investigation
- Chase

Target AI

Features:

- Escape
- Route Planning

---

# Main Scene

Open:

Assets/Scenes/MainSandbox.unity

Press Play.

The map will be generated at runtime.

---

# Controls

WASD : Move

Space : Jump

Mouse : Camera

---

# Repository Structure

Assets/
│
├── Scenes/
├── Scripts/
│ ├── PCG/
│ ├── Navigation/
│ ├── AI/
│ └── Player/
│
├── Prefabs/
├── Materials/
├── Models/

Packages/
ProjectSettings/

---

# Known Issues

- PCG currently generates runtime-only maps.
- Some Asset Store assets must be imported manually.
- Pathfinding evaluation is under development.

---

# Authors

Hins-Wei, Lin
AI for Games
NCKU Spring 2026
