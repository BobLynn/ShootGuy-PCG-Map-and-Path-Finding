# ShootGuy PCG Map and Path Finding

合作 repo：Shoot-Guy-Guard-and-Target-AI-Systems-for-Hitman-Style-Gameplay

這是《AI for Games》期末專案的 Unity 專案，目前包含：

* PCG 地圖生成
* Grid Map / Waypoint Graph
* Pathfinding 測試
* 玩家機器人控制與測試場景

## Unity 版本

請使用以下 Unity 版本開啟：

```txt
Unity 6000.4.5f1
```

使用其他版本可能會出現套件不相容、場景引用遺失或材質錯誤。

## 如何在另一台電腦開啟

### 1. Clone 專案

先安裝 Git，然後在想放專案的位置執行：

```bash
git clone https://github.com/BobLynn/ShootGuy-PCG-Map-and-Path-Finding.git
```

### 2. 用 Unity Hub 開啟

1. 打開 Unity Hub
2. 點選 `Add / Open`
3. 選擇剛剛 clone 下來的資料夾：

```txt
ShootGuy-PCG-Map-and-Path-Finding
```

4. 使用 `Unity 6000.4.5f1` 開啟專案

第一次開啟時 Unity 會重新生成 `Library/`，所以會比較久。

### 3. 開啟測試場景

目前主要測試場景在：

```txt
Assets/Scenes/PCGScene_1.unity
```

也可以查看：

```txt
Assets/Scenes/AptScene.unity
```

### 4. 按 Play 測試

開啟場景後按下 Play，即可測試目前的 PCG 地圖、玩家機器人與 pathfinding 相關功能。

## 注意事項

本 repo 不包含 Unity 自動生成的暫存資料，例如：

```txt
Library/
Logs/
Temp/
Obj/
Build/
```

這些資料夾會由 Unity 自動重新產生，不需要手動加入 Git。

如果場景出現材質或模型遺失，請確認是否有使用到未包含在 repo 內的外部素材或 Asset Store 資產。
