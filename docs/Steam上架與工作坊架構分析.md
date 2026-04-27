# Steam 上架 + 工作坊整合 架構分析

> 寫於 2026-04-27（commit `7be6bf0` 任務 11 ship 後）。
> 對照規格書 §6 里程碑 + Steam Workshop API 文件。
> 目的：以「Steam 上架且支援 Workshop」為前提，盤點需新增與需完成的架構。

---

## 1. 上架 Steam 的兩層要求

```
┌──────────────────────────────────────────────────────┐
│         Layer A：任何 Steam 遊戲都需要              │
│  ─ Steamworks SDK 整合                               │
│  ─ App ID / Build pipeline                           │
│  ─ 雲端存檔 / 成就 / 統計（可選但推薦）              │
│  ─ 多語言（至少 zh-TW + en）                         │
│  ─ 設定選單 / 鍵位配置                               │
│  ─ 控制器支援（Big Picture 模式可選）                │
│  ─ 商店頁素材（封面 / 截圖 / Trailer）               │
│  ─ 隱私政策 / 終端使用者協議（EULA）                 │
└──────────────────────────────────────────────────────┘
                          ↓
┌──────────────────────────────────────────────────────┐
│         Layer B：工作坊整合特定                       │
│  ─ ISteamUGC API（建立 / 更新 / 訂閱）               │
│  ─ 模組 ↔ Workshop Item 對映                         │
│  ─ 訂閱流程 / 自動下載 / 啟用                        │
│  ─ 遊戲內模組製作工具                                │
│  ─ 法律協議 / 內容守則                                │
│  ─ 模組瀏覽 / 評分 / 過濾 UI                          │
└──────────────────────────────────────────────────────┘
```

---

## 2. 整體 Gap 分析

### 2.1 ✅ 已完成（可直接用於 Steam）

| 項 | 狀態 |
|---|---|
| 模組 = 資料夾結構 | ✅ 與 Workshop 資料夾式打包對齊 |
| Manifest 元資料可獨立查詢 | ✅ ModuleLoader 不需載完整內容就能 parse manifest |
| Schema 驗證 | ✅ 10 種 JSON Schema |
| Cross-ref 驗證 | ✅ ModuleLoader |
| 模組 immutable 載入 | ✅ Module record |
| 引擎與內容分離 | ✅ 完全 module-agnostic |

### 2.2 🔴 必補（Steam 上架前提）

| 項 | 影響 | 任務歸屬 |
|---|---|---|
| 完整劇本可通關 | 至少 1 條 ending 達門檻可觸發 | 任務 14 |
| 戰鬥子系統 | triggerBattle 真的能打 | 任務 13 |
| 訊息氣泡系統 | flag/resource UI 反饋 | 任務 12 |
| 美術 PNG 替換 SVG | Linocut 風格 | 任務 15 |
| 音效 / BGM | ≥5 動畫 + BGM | 任務 16 |
| 存檔讀檔 + 設定選單 | 自動 + 3 槽手動 | 任務 17 |
| 多語言（至少 EN） | i18n 系統 | 規格書未列，需加 |
| 控制器支援 | Big Picture mode | 規格書未列，可選 |
| 設定選單（解析度 / 音量 / 鍵位） | 任務 17 內 | 任務 17 |

### 2.3 🔴 工作坊整合（全新）

| 項 | 規模 | 對應 Steam API |
|---|---|---|
| Steamworks SDK 整合 | 大 | `Steamworks.NET` / GodotSteam |
| Workshop item 建立 / 上傳 UI | 大 | `ISteamUGC::CreateItem` / `StartItemUpdate` |
| 訂閱 / 安裝 / 更新流程 | 中 | `SubscribeItem` / `GetItemInstallInfo` / `ItemInstalled_t` |
| 模組 ID ↔ PublishedFileId 對映 | 中 | `PublishedFileId_t` |
| 模組瀏覽 / 評分 UI | 大 | `ISteamUGC::CreateQueryAllUGCRequest` |
| 工作坊頁面內嵌 / 開啟外部 | 小 | `ActivateGameOverlayToWebPage` |
| 內容警告系統 | 中 | tags / metadata |
| 法律協議 dialog | 小 | UGC Agreement |

---

## 3. 全部需新增 / 補完的組件（完整清單）

### 3.1 引擎核心補完（Phase 2 + 3 任務 12-17）

#### 任務 12：訊息氣泡系統（**6-8 h**）
- MessageBubbleService
- 訊息泡 UI 區塊（區塊 #4）
- effect → 訊息泡 hook（setFlag / grantResource 顯示）
- 摺疊 / 點開 / 跳轉協作（→ ORBIT、→ 同伴卡）

#### 任務 13：戰鬥子系統（**12-16 h**）
- BattleEngine runtime 整合（後端已有，UI 待）
- 4 階段戰鬥子場景（Reveal / Plan / Execute / Resolve）
- 同伴 3 種戰鬥輔助（守衛 / 支援 / 分擔傷害）
- LootEffects（戰鬥後 grantEquipment）
- 完整 EffectsService（vulnerable / buff / debuff，§1.11 完整版）

#### 任務 14：完整劇本 + 多結局分支（**20-30 h**）
- EndingService（觸發 / 統計 / 結局畫面）
- 完整 EventResolver（含 RollCheck / dice）走 runtime
- TurnLoop 完整接 runtime（取代 WorldMap.AdvanceTurn）
- TileProgressService 接 UI
- NpcAi（同伴自動出牌）
- abandoned-mansion 通關測試 + 平衡調整
- 統計面板（總回合 / 解算事件數 / 戰鬥勝負 / 貢獻分數）

#### 任務 15：美術替換（**待美術師 + 8-12 h 整合**）
- tools/ArtRasterizer 批次處理
- 全 PNG（角色立繪 / 卡面 / 地塊頂面 / 場景立繪三層 / UI 元件 / 訊息氣泡 / 同伴卡）
- 視覺風格 Linocut + navy/cream

#### 任務 16：音效 / BGM / UI 動畫（**10-15 h**）
- BGM 切換（村莊 / 洋房 / 戰鬥 / 結局）
- 音效（出牌 / 移動 / 命中 / 結算）
- ≥5 個關鍵動畫

#### 任務 17：存檔讀檔 + 設定選單 + 教學（**12-18 h**）
- SaveService（自動 + 3 槽手動）
- GameState 序列化 / 反序列化
- 系統選項區塊（區塊 #6）：解析度 / 音量 / 字型大小 / 鍵位
- 新手引導 / 教學 overlay

**Phase 2-3 任務小計：約 70-105 h**

---

### 3.2 模組系統強化（Steam 上架 + Workshop 前提）

#### 模組可替換 runtime（**4-6 h**）
- DiscoverModules 掃 modules/ 目錄
- 主選單模組選擇 UI
- 失敗隔離 + 錯誤訊息

#### 模組 ID 命名空間化（**1 h**）
- Schema 強制 `author/id` 格式
- 既有 `abandoned-mansion` → `haloflag/abandoned-mansion`

#### Manifest 補強（**2-3 h**）
- coverImage / thumbnail
- themes / contentWarnings
- minimumEngineVersion
- supportedLanguages

#### 模組 lint 工具（tools/ModuleLinter，**6-8 h**）
- Dead reference / unused tile 檢查
- 通關可達性 BFS
- 平衡性提示

#### 多語言 i18n（**8-12 h**）
- 模組內 narrative / name 多語言（建議 dict 形式）
- UI 文字 i18n 機制
- 至少支援 zh-TW + en
- locale 切換選單

**模組系統小計：約 21-30 h**

---

### 3.3 Steamworks SDK 整合（全新，**大塊頭**）

#### 核心 SDK 整合（**8-12 h**）
- 選 GodotSteam 插件 OR 自建 Steamworks.NET wrapper
- Steam App ID 申請（$100 USD Steam Direct Fee）
- SteamAPI_Init / SteamAPI_RunCallbacks
- 啟動時驗證 Steam 連線
- App 退出時 SteamAPI_Shutdown
- Steam 未啟動時的 graceful degradation

#### 雲端存檔 Steam Cloud（**4-6 h**）
- 設定 Cloud Quotas（Steamworks 後台）
- 存檔目錄改用 `SteamRemoteStorage`
- 衝突解決（本機 vs 雲端）

#### 成就 / 統計（**6-10 h**）
- 設計成就清單（10-20 個）
  - 「首次通關標準難度」
  - 「達成 S 評等結局」
  - 「100% 揭露所有事件」
  - 「不死通關」
  - 「使用所有 4 個角色通關」
- 後台註冊 → 程式碼 SetAchievement
- 統計：總遊玩時間 / 通關次數 / 最高評分

#### Steam Overlay 整合（**3-4 h**）
- ActivateGameOverlay
- 截圖鍵 / 廣播
- Steam friends UI（即使 single-player 也常用）

#### Build pipeline（**4-6 h**）
- steamcmd 設定 VDF 檔
- depot 配置（Windows / macOS / Linux）
- 上傳腳本

**Steamworks SDK 小計：約 25-38 h**

---

### 3.4 工作坊整合（**最複雜**）

#### Workshop 模組 ↔ 本地模組對映（**6-8 h**）
- `PublishedFileId_t (uint64)` ↔ `<author>/<id>` 對應表
- modules/workshop/<PublishedFileId>/ 目錄
- 從 Workshop 載入時，引擎透過此對映找到模組
- 處理本機已有 ID 衝突

#### Workshop 訂閱流程（**8-12 h**）
- 主選單顯示「我的訂閱」「瀏覽工作坊」
- ISteamUGC.SubscribeItem / UnsubscribeItem
- ItemInstalled_t / ItemUpdated_t callback 處理
- 自動載入訂閱模組
- UI 顯示下載進度
- 訂閱失敗 / 安裝失敗處理

#### Workshop 模組瀏覽 UI（**12-16 h**）
- 遊戲內 Workshop 瀏覽器
- 條件查詢（標籤 / 評分 / 訂閱數）
- 預覽圖 + 描述 + 評分顯示
- 訂閱 / 取消訂閱按鈕
- 評分 / 評論連結（外部 Steam Overlay）

#### 模組製作 UI（in-game uploader）（**16-20 h**）
- 從 modules/dev/ 上傳到 Workshop
- ISteamUGC.CreateItem → 取得 PublishedFileId
- StartItemUpdate / SetItemTitle / SetItemDescription / SetItemContent / SetItemPreview / SetItemMetadata
- SubmitItemUpdate
- 進度條 + 失敗處理
- UGC Agreement 同意 dialog

#### 內容守則 / 警告系統（**4-6 h**）
- 玩家上傳前同意 Steam UGC Agreement
- contentWarnings → Steam tags 對映
- 兒少不宜內容警告（成人標記）

#### 更新 / 版本管理（**4-6 h**）
- 訂閱模組有更新時通知
- 玩家自製模組的版本號管理
- 模組 schema 不相容時警告

**工作坊整合小計：約 50-68 h**

---

### 3.5 商店頁素材（行銷，非程式）

| 項 | 規模 |
|---|---|
| 商店頁封面（460×215, 920×430, header）| 美術 |
| 截圖（≥5 張，1920×1080） | 美術 |
| Trailer 影片（30s-2min） | 美術 + 剪輯 |
| 描述文案 | 文案 |
| 標籤 / 分類設定 | 後台設定 |
| 多國語言商店頁（zh-TW + en 至少） | 翻譯 |
| 評等申請（IARC） | 法務 |

非程式工時，但需排期。

---

## 4. 全部工時統整

```
Phase 2-3 任務 12-17 ........... 70-105 h（核心遊戲）
模組系統強化 .................... 21-30 h（替換 / lint / i18n）
Steamworks SDK 整合 ............. 25-38 h（成就 / 雲端 / build）
工作坊整合 ...................... 50-68 h（訂閱 / 上傳 / 瀏覽）
─────────────────────────────────────────
程式總計 ....................... 166-241 h（約 4-6 週全職）

商店頁素材 ..................... 美術 / 翻譯 / 文案 另計
```

---

## 5. 推薦執行 Roadmap

### Phase A：核心遊戲完整性（先 ship 才能談 Workshop）

```
1. 任務 12 訊息氣泡    （6-8 h）
2. 任務 13 戰鬥子系統  （12-16 h）
3. 任務 14 完整劇本    （20-30 h）
   ├ 至少 1 個結局可觸發
   ├ abandoned-mansion 通關測試
   └ 平衡調整
4. 任務 17 存檔 + 設定選單 + 教學（12-18 h）
5. 任務 16 音效 / BGM（10-15 h）
6. 任務 15 美術替換（8-12 h + 美術師）

合計：68-99 h
```

→ 結束時：**遊戲完整可通關，可單機 ship**

### Phase B：模組系統強化

```
7. 模組可替換 runtime（4-6 h）
8. 模組 ID 命名空間 + manifest 補強（3-4 h）
9. 模式 1 難度 preset（5 h）
10. 多語言 i18n 機制（8-12 h）
11. 模組 lint 工具（6-8 h）

合計：26-35 h
```

→ 結束時：**多模組 / 多語言 / 多難度可玩**

### Phase C：Steamworks SDK

```
12. SDK 整合 + App ID（8-12 h）
13. 雲端存檔（4-6 h）
14. 成就 / 統計（6-10 h）
15. Steam Overlay（3-4 h）
16. Build pipeline（4-6 h）

合計：25-38 h
```

→ 結束時：**Steam 上架就緒（無 Workshop）**

### Phase D：工作坊整合

```
17. Workshop ↔ 本地模組對映（6-8 h）
18. 訂閱流程（8-12 h）
19. Workshop 瀏覽 UI（12-16 h）
20. 模組製作 UI（16-20 h）
21. 內容守則（4-6 h）
22. 更新管理（4-6 h）

合計：50-68 h
```

→ 結束時：**完整工作坊整合**

---

## 6. 關鍵架構設計決策（**現在就要定**）

### 6.1 Workshop ID 與本地 ID 對映策略

**問題**：Steam 用 `PublishedFileId_t (uint64)`，我們用 `<author>/<id>` 字串。需要 bridging。

**建議**：
```json
// 模組 manifest.json
{
  "id": "haloflag/abandoned-mansion",
  "workshopId": null,                  ← null 表示本地模組（未發布）
  "version": "1.0.0",
  ...
}
```

模組首次上傳 Workshop 時，引擎呼 `CreateItem` 取得 PublishedFileId → 寫回 manifest.json。

引擎掃描模組時：
- 有 workshopId → 視為「已發布」（顯示 Workshop 連結）
- 無 workshopId → 視為「本地」（可發布按鈕）

### 6.2 模組目錄結構（含 Workshop）

```
modules/
├── builtin/                              ← 系統內建（隨遊戲安裝）
│   └── haloflag/abandoned-mansion/
├── workshop/                             ← Workshop 訂閱（自動同步）
│   ├── 1234567890/                       ← PublishedFileId 為資料夾名
│   │   └── (展開的模組內容)
│   ├── 2345678901/
│   └── _registry.json                    ← workshopId ↔ namespacedId 對映
├── user/                                 ← 玩家手動安裝
│   └── community-author/haunted-house/
└── dev/                                  ← 開發中
    └── my-test-module/
```

引擎啟動掃描順序：builtin → workshop → user → dev。

### 6.3 模組製作工作流程

**建議「dev → 自家測試 → 上傳 Workshop」三階段**：

```
1. 模組製作者開發：
   modules/dev/my-module/  
   - 改 JSON
   - 在引擎內 dev mode 載入測試
   - schema lint 通過

2. 自家測試（可選）：
   把資料夾複製到 modules/user/<author>/<id>/
   - 模擬「使用者裝了我的模組」狀態
   - 測試正常選單流程

3. 上傳 Workshop：
   遊戲內「上傳模組」按鈕
   - 選 dev/ 內模組
   - 填 title / description / tags / preview
   - 同意 UGC agreement
   - 上傳 → 取得 PublishedFileId
   - 寫回 manifest.workshopId
```

### 6.4 多語言策略選擇（**關鍵決策**）

**選項 A：模組分語言版本**
- `haloflag/abandoned-mansion-zh-TW`
- `haloflag/abandoned-mansion-en`
- 簡單但複本多

**選項 B：欄位內嵌多語言**
```json
{
  "narrative": {
    "zh-TW": "...",
    "en": "..."
  }
}
```
- Schema 改大但模組單一
- 引擎依玩家 locale 取對應字串

**推薦**：選項 B + manifest.supportedLanguages 顯示給玩家。

### 6.5 模組相容性 / 版本控管

引擎進化時 schema 會變。建議：
- 引擎宣告 `engineVersion: 1.0.0` + `schemaVersion: 2`
- 模組宣告 `minimumEngineVersion: 1.0.0` + `schemaVersion: 1`
- 載入時：
  - schemaVersion <= 引擎當前 → 用對應版本 schema 驗證
  - minimumEngineVersion > 引擎 → 提示「請更新遊戲」

---

## 7. 風險點 / 不確定性

| 風險 | 評級 | 緩解 |
|---|---|---|
| **Godot 4 Steamworks 整合穩定性** | 🟡 中 | GodotSteam 社群活躍但非官方；先做 spike 驗證 |
| **Steam Direct Fee（$100）** | 🟢 低 | 一次性；上架前確認 |
| **Workshop 內容審核成本** | 🟡 中 | 玩家上傳的模組可能有不適內容；需 contentWarnings + 報告機制 |
| **多語言翻譯成本** | 🔴 高 | abandoned-mansion 約 1500 行 narrative；專業翻譯 ~$1000 USD |
| **美術成本** | 🔴 高 | Linocut 風格 PNG 約 ≥50 張；外包 $5000+ USD |
| **音效 / BGM 授權** | 🟡 中 | 用免費 royalty-free 或外包 |
| **規格書 §3.5 SubViewport 重構** | 🟢 低 | 任務 16/17 觸發時再做 |
| **任務 14 後端整合複雜度** | 🟡 中 | TurnLoop / NpcAi 接 runtime 是大工程；可能超估時 |
| **Workshop 模組相依性處理** | 🟡 中 | Mode 3 extends 若使用者只訂 overlay 沒訂 base 會壞；需 Workshop dependencies API |
| **跨平台支援** | 🟡 中 | 至少 Win + Mac + Linux；Godot 通常跨平台 OK 但 Steamworks 需各平台測 |

---

## 8. 競品 / 參考案例

| 遊戲 | Workshop 整合方式 | 學習點 |
|---|---|---|
| **Slay the Spire** | mods 透過 Workshop + ModTheSpire DLL | 程式 plugin 模型 |
| **Crusader Kings 3** | total conversion mods | 大型內容 mod |
| **RimWorld** | Workshop + 模組相依性圖 | 複雜相依管理 |
| **Cards Against Humanity-style 卡牌遊戲** | 純資料 mod | 我們最像這類 |

→ 我們**純 JSON 模組**最像 RimWorld 的 simpler mods。Workshop 整合可參考 RimWorld 的 ModConfigs.xml 機制。

---

## 9. Steam 商店上架準備（非程式但重要）

### 9.1 Steam Direct 流程

1. 註冊 Steamworks Developer 帳號（免費）
2. 完成 W-8BEN / W-9 稅務表格
3. 支付 $100 USD Steam Direct Fee
4. 取得 App ID
5. 等待 30 天 Trust Period
6. 上架前 60 天填商店頁（Coming Soon）
7. 內容評級（IARC）申請
8. 完成後可正式上架

### 9.2 商店頁必備

- 短描述（300 字內）
- 詳細描述（含 Markdown）
- 系統需求
- 截圖 ≥5 張
- 封面圖（460×215）
- Header 圖（920×430）
- Trailer（建議）
- 多語言商店頁（建議 zh-TW + en）
- 標籤（10-20 個）
- 內容警告

---

## 10. 總結 — 上架 Workshop 完整路線圖

| Phase | 內容 | 工時 | 累計 |
|---|---|---|---|
| **A** | 核心遊戲（任務 12-17） | 68-99 h | 68-99 |
| **B** | 模組系統強化 | 26-35 h | 94-134 |
| **C** | Steamworks SDK | 25-38 h | 119-172 |
| **D** | 工作坊整合 | 50-68 h | 169-240 |
| **E** | 商店頁素材 + 翻譯 + 美術 | 不算工時，外部 | (額外資源) |
| **F** | Steam Direct 流程 | 不算工時，30+ 天等待 | (時間) |

**最樂觀估時**：4 週全職（169 h）+ Steam Direct 30 天等待 + 美術翻譯外包週期 ≈ 約 3-4 個月可上架。

**最關鍵 4 件事必做**：
1. ✅ **任務 14 通關** — 必須有完整可玩劇本（不能上架空殼）
2. ✅ **模組可替換 runtime** — 沒有這個，工作坊毫無意義
3. ✅ **Steamworks SDK 基本整合** — 上架最低門檻
4. ✅ **多語言 i18n** — Workshop 國際社群必須

**可延後的**：
- ⏸ 完整工作坊瀏覽 UI（v1 可只支援訂閱 + 自動載入，瀏覽用 Steam Overlay）
- ⏸ 遊戲內模組製作 UI（v1 可要求模組製作者用 steamcmd 上傳）
- ⏸ 模組 lint 工具（v1 用 schema 驗證 + 通關測試夠了）

---

## 11. 模組實作通用注意事項（從 Steam Workshop 學到）

> 整理 Steam Workshop API 文件對任何模組系統的設計啟發。

### 11.1 必做的 4 個原則

1. **每個模組一個資料夾**（已有 ✅）
2. **Manifest 是模組身分卡，不下載完整內容就能查詢**（部分 ✅，需加 cover/themes/dependencies）
3. **唯一識別 ID + 命名空間**（缺 ❌，需加 author/id 規範）
4. **失敗隔離 + 不影響其他模組**（缺 ❌，需加錯誤處理）

### 11.2 專案特化考量

5. 多模組 + 啟用 / 停用機制
6. 相依性圖 + 拓撲載入
7. Schema 版本相容
8. 模組 lint 工具（Dead ref / 可達性）

### 11.3 Steam 不適用部分

我們的模組系統本質是**離線 / 本機 modding**，比 Steam Workshop 簡單很多。Steam 的價值主要在**架構原則**而非具體 API：

| Steam 機制 | 為什麼不適用 |
|---|---|
| `PublishedFileId_t` 服務器發配 ID | 我們無中央服務器，用 namespaced string 即可 |
| 不可撤回更新 | 本機修改可隨意改 |
| 訂閱 API | 我們直接掃資料夾，不需 API（但 Workshop 整合時需要） |
| 法律協議 | 開放平台才需要 |
| 認證 / 審核流程 | 同上 |
| 雲端同步 | Phase 3 存檔系統再考慮 |
