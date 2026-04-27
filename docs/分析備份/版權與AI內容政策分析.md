# 兩份 Steam 官方文件中關於版權 / AI 內容 / 美術的規定分析

> 寫於 2026-04-27（commit `7be6bf0` 後）。
> 來源文件：
> - [Steam Workshop Implementation Guide](https://partner.steamgames.com/doc/features/workshop/implementation?l=tchinese)
> - [Steam Content Survey](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)
>
> 補充參考：
> - [Steam AI Content Policy Announcement](https://steamcommunity.com/groups/steamworks/announcements/detail/3862463747997849619)
> - [The Register - Steam AI Disclosure (2024)](https://www.theregister.com/2024/01/10/developers_steam_ai/)
> - [GeekWire - Valve AI Rules](https://www.geekwire.com/2024/valve-software-reveals-new-rules-for-ai-powered-game-development-on-steam/)
> - [Argo Law - Steam AI Policy Analysis](https://argolawyer.com/what-really-changed-with-steams-new-rules-on-ai-generated-art/)
> - [Digital Watch Observatory - 2026/01 Update](https://dig.watch/updates/new-steam-rules-redefine-when-ai-use-must-be-disclosed)

---

## 1. 版權（Copyright）規定

### 1.1 Steam Distribution Agreement 核心承諾

兩份文件都引用 Steam Distribution Agreement，對版權的核心要求是：

> **「you promise Valve that your game will not include illegal or infringing content」**
> （你向 Valve 承諾，你的遊戲不會包含非法或侵權內容）

這個承諾**完全把法律責任落到開發者身上**。Valve 不承擔審查或擔保責任。

### 1.2 第三方版權處理

- Valve 提供 **DMCA 版權侵害通知表單**
- 提供 **商標投訴表單**
- 任何第三方可以提交侵權通知 → 觸發下架 / 鎖定流程

### 1.3 Workshop（玩家上傳）特殊要求

Workshop 文件提到：

- **預設物品不公開**（visibility 必須明確設定）— 防止意外曝光未授權內容
- **玩家上傳前須同意 UGC 法律協議**（Steam Subscriber Agreement + Steam UGC Workshop Agreement）
- 建議「在提交按鈕旁說明法律協議」

### 1.4 對 CardNarrative 專案的含意

| 議題 | 我們的狀況 | 必做事項 |
|---|---|---|
| 引擎程式碼版權 | 自寫 + Godot 4 (MIT) + .NET (MIT) + xUnit (Apache 2.0) + 套件 | ✅ 開源協議都相容；發行前確認 third-party-licenses 清單 |
| **abandoned-mansion 模組所有 narrative** | 自寫（中文敘事） | ✅ 自有版權，可上 |
| **角色立繪 / 場景圖** | 目前是 placeholder（人物A/B/前景.png 是抽象圖） | 🔴 Task 15 美術替換時必須確認原創或購買授權 |
| **音效 / BGM**（未實作） | 未來 Task 16 需處理 | 🔴 royalty-free / 自製 / 購買授權 |
| **字型** | Noto Sans CJK / 其他 Godot 內建 | ⚠️ 確認字型授權允許商用 |
| **Workshop 模組製作者上傳** | 我們需提供 UGC Agreement dialog | 🔴 上傳 UI 必須有法律協議勾選 |
| **舉報違規模組機制** | 無 | 🟡 至少有「舉報」按鈕導向 Steam Overlay |

---

## 2. AI 生成內容規定（**最關鍵 / 最新政策**）

### 2.1 兩類 AI 內容分類

Steam 自 2024 年 1 月起明確區分：

| 類別 | 定義 | 例子 |
|---|---|---|
| **Pre-Generated AI 內容** | 開發期間用 AI 工具產生的內容（美術 / 程式 / 音效 / 文字 / 在地化等） | Midjourney 畫的角色立繪、Stable Diffusion 的場景背景、ChatGPT 寫的 narrative、AI 翻譯 |
| **Live-Generated AI 內容** | 遊戲執行時即時生成的內容 | 遊戲內 LLM 對話、實時生成的 NPC 對話、即時 AI 美術 |

### 2.2 開發者揭露義務

兩類都必須在 **Steam Content Survey** 中揭露：

#### Pre-Generated 揭露要求

開發者必須**承諾**：
- 「pre-generated materials don't include any illegal or infringing content」
- 不誤導玩家（marketing 必須誠實）
- 簽署後法律責任**完全在開發者**

#### Live-Generated 揭露要求

開發者必須**詳細說明**：
- 「what guardrails are put in place to prevent AI from making anything inappropriate or unlawful on-the-fly」
- 即：必須描述 AI 實時生成的**護欄機制**

### 2.3 絕對禁止項

- **Live-Generated AI Adult Only Sexual Content 完全禁止上架**
- 違反 = 拒絕 / 下架

### 2.4 玩家端機制

- Steam 會**在商店頁標示遊戲使用 AI 內容**
- 玩家可**舉報違規 AI 內容**
- 玩家視為「enforcement partner」（執法夥伴）

### 2.5 2026 年 1 月政策更新（最新）

- **僅限玩家會看到 / 接觸到的 AI 內容**需揭露
  - 包括：美術、音訊、在地化、敘事、行銷素材、商店頁
- **純開發期工具**不需揭露
  - 例：用 ChatGPT 寫 dev tools、用 Copilot 自動補完程式碼

這是個**重要鬆綁**：撰寫遊戲程式碼時用 AI 不需揭露，只要玩家「看不到」AI 痕跡。

### 2.6 對 CardNarrative 專案的含意

#### 我們的 AI 使用現況盤點

| 項 | AI 使用 | 是否需揭露 |
|---|---|---|
| 引擎程式碼（C#） | 開發期可能用 AI 輔助寫 | ❌ 不需揭露（開發工具） |
| 規格書 / 架構文件 | 寫文件時可能用 AI 輔助 | ❌ 不需揭露 |
| **abandoned-mansion narrative**（13 events / 4 endings / 19 tile descriptions） | **如果用 AI 生成** | ✅ **必須揭露為 Pre-Generated** |
| **角色立繪**（Task 15） | 若用 Midjourney / SD 生成 | ✅ **必須揭露為 Pre-Generated** |
| **場景立繪**（Task 15） | 同上 | ✅ 必須揭露 |
| **卡片美術**（Task 15） | 同上 | ✅ 必須揭露 |
| **音效 / BGM**（Task 16） | 若用 Udio / Suno 等 | ✅ 必須揭露 |
| **runtime 對話 / 動態 narrative** | 我們**沒有**接 LLM | ❌ 無需揭露 |

#### 關鍵決策點

如果**任何**玩家可見內容用 AI 生成 → 必須在商店頁揭露 → 玩家會看到「This game uses AI-generated content」標籤。

**對銷售有實質影響**：
- 部分玩家會**主動避開**有 AI 標籤的遊戲
- 反 AI 社群會集中評論
- 但也有玩家不在意

#### Workshop 模組的 AI 內容

**Steam 文件未明確規範**模組製作者上傳的 AI 內容是否需揭露。但合理推論：

- 玩家上傳模組屬 UGC（User-Generated Content）
- UGC Agreement 已要求「合法 + 不侵權」
- 若模組大量使用 AI，從風險管理角度建議我們的 **UGC Agreement dialog 加上 AI 揭露勾選欄**

---

## 3. 美術圖（Art）規定

### 3.1 Steam 對美術版權的明確要求

從 Steam Distribution Agreement + Content Survey 推導：

| 項 | 規定 |
|---|---|
| 遊戲內美術 | 不可侵權（DMCA 風險） |
| 商店頁封面 / 截圖 / Trailer | 同上 |
| Workshop 模組 preview image | 同上 |
| 人物 / 角色立繪 | 不可使用受保護的智慧財產（如「皮卡丘」「米奇老鼠」等） |
| AI 生成美術 | 必須揭露為 Pre-Generated AI（見 §2） |

### 3.2 法律灰色地帶

#### AI 生成美術的版權歸屬

- 美國法院（2023 Thaler v. Perlmutter）：**純 AI 生成作品無版權**
- 中國北京網際網路法院（2023）：**有「智慧財產投入」的 AI 作品有版權**
- Valve 立場：**版權責任完全在開發者** — 你說沒侵權，Valve 信你；後果你扛

#### 「fair use / 合理使用」

- Steam 不擔任版權審查者
- 玩家舉報 → DMCA 通知 → 開發者反通知 → 法庭解決

### 3.3 對 CardNarrative 專案的含意

#### 當前狀況檢查

```
art/
├── portraits/
│   ├── 人物A.png        ← 目前是抽象人形剪影（placeholder）
│   └── 人物B.png        ← 同上
├── scenes/
│   └── 前景.png          ← placeholder
├── tiles/
│   └── *.png             ← 6 種地形（forest/path/grass/water/mountain/building）
├── cards/
├── ui/
└── ...
```

⚠️ **正式發布前必須**：
- 替換 placeholder 為**正式美術**（Task 15）
- 確認每張 PNG 來源
- 若用 AI → 在 Content Survey 揭露
- 若購買 / 委託 → 保留授權證明
- 若 CC0 / public domain → 文件記錄來源

#### Task 15 美術替換的決策矩陣

| 美術來源選項 | 成本 | 揭露需求 | 風險 |
|---|---|---|---|
| **委託專業美術師**（推薦） | 高（$5000+ USD） | 不揭露 | 🟢 低 |
| **購買 stock asset**（如 Itch.io 商業 license 包） | 中（$50-500） | 不揭露 | 🟢 低（須留證） |
| **CC0 / public domain** | 免費 | 不揭露 | 🟢 低（須留證） |
| **AI 生成**（Midjourney / SD / DALL-E） | 低 | ✅ **必須揭露** | 🔴 高（玩家社群反彈、版權灰色） |
| **自己畫** | 時間 | 不揭露 | 🟢 低 |

→ **強烈建議避開 AI 生成美術**，除非有具體商業考量。原因：
1. Steam 揭露標籤對銷售有實質影響
2. 版權灰色地帶（Stable Diffusion 訓練資料正在多國訴訟）
3. 玩家社群（特別是獨立遊戲圈）對 AI 美術敏感
4. 美術風格一致性難維持（commissions 容易做到）

#### 模組製作者的美術

Workshop 模組若可自帶美術（之前的「模組自帶美術」項目），需考慮：

- 模組製作者上傳前**必須勾選**「我擁有美術版權或有合法授權」
- 模組製作者使用 AI 美術 → 模組 manifest 加 `aiContent: true` 欄位
- 我們的引擎可在模組選擇 UI 顯示 AI 標籤
- DMCA 通知到達 → 我們配合 Valve 下架該模組

---

## 4. 對 CardNarrative 專案的具體影響清單

### 4.1 必做（Steam 上架前提）

| # | 項 | 任務 |
|---|---|---|
| 1 | 替換所有 placeholder 美術為正式版 | Task 15 |
| 2 | 全美術版權記錄 / 授權證明歸檔 | Task 15 |
| 3 | 商店頁素材（封面 / 截圖 / Trailer）原創或授權 | 上架準備 |
| 4 | 所有音效 / BGM 版權清單 | Task 16 |
| 5 | 字型授權確認（商用） | 上架準備 |
| 6 | 完成 Steam Content Survey AI 揭露 | 上架準備 |
| 7 | DMCA 聯絡窗口設置 | 上架準備 |

### 4.2 Workshop 整合必做

| # | 項 | 任務 |
|---|---|---|
| 8 | UGC Agreement dialog（玩家上傳前同意） | Phase D 工作坊整合 |
| 9 | 上傳前的 AI 內容勾選欄 | Phase D |
| 10 | 上傳前的版權聲明勾選 | Phase D |
| 11 | manifest 加 `aiContent` / `contentLicense` 欄位 | Phase B 模組強化 |
| 12 | 模組選擇 UI 顯示 AI / 版權標籤 | Phase D |
| 13 | 「舉報模組」按鈕導向 Steam Overlay | Phase D |

### 4.3 強烈建議

| # | 項 | 理由 |
|---|---|---|
| 14 | **避開 AI 生成美術** | 銷售衝擊 + 版權風險 + 社群反彈 |
| 15 | **narrative 自寫**（不用 LLM 生成主線敘事） | 同上 |
| 16 | 開發期用 AI 輔助 OK（程式碼 / dev tools） | 不需揭露，效率提升 |
| 17 | 文件 / 規格書用 AI 輔助 OK | 玩家看不到 |

---

## 5. 模組製作者注意事項（社群版）

我們應在**模組製作指南**中明確告知社群：

### 5.1 必須做的事

1. **所有素材必須原創或有合法授權**
2. **AI 生成素材必須在 manifest 標記**（`aiContent: true`）
3. **不可使用受保護的智慧財產**（無 IP 衝突的角色 / 場景）
4. **narrative 不可侵犯他人作品**（不可整段抄襲書 / 電影 / 動漫）
5. **上傳 Workshop 前同意 UGC Agreement**

### 5.2 建議做的事

1. 美術風格與遊戲基調協調（避免破壞引擎美感）
2. 避免使用 Live-Generated AI 內容（即時 LLM）
3. 在模組描述中說明素材來源
4. 模組頁面提供作者聯絡方式（DMCA 反通知用）

### 5.3 工具支援

引擎可提供 lint 工具：
- 檢查 manifest 是否聲明 contentLicense
- 檢查是否有 AI 揭露
- 檢查美術檔案 metadata 是否完整

---

## 6. 風險點 / 不確定性

| 風險 | 嚴重度 | 緩解 |
|---|---|---|
| 用 AI 美術導致 Steam 標籤 → 銷量影響 | 🔴 高 | 委託 / 購買 / 自畫，避開 AI |
| 模組製作者上傳侵權內容 | 🔴 高 | UGC Agreement + 舉報機制 + DMCA 配合 |
| 模組 AI 內容無揭露 | 🟡 中 | 引擎強制要求 manifest 揭露 |
| 字型 / 音效授權忘記留證 | 🟡 中 | 維護 third-party-licenses 清單 |
| Steam AI 政策後續變化 | 🟡 中 | 定期重看官方政策（每半年） |
| 社群「AI 偵測」誤判 | 🟢 低 | 留作品建構過程證明（layered PSD / 草稿） |
| Live-Generated AI 含成人內容（誤觸） | 🔴 高（自動下架） | 我們無 LLM 接入，風險 0 |

---

## 7. 兩份文件規定核心摘要表

| 議題 | Workshop 文件 | Content Survey 文件 |
|---|---|---|
| **版權** | 「Workshop 預設不公開」 + UGC Agreement | 「不可含侵權內容」承諾 + DMCA 流程 |
| **AI 揭露** | 未提（Workshop 範疇） | ✅ 強制揭露（Pre + Live） |
| **美術版權** | preview image 屬內容範疇 | 視覺資產不得侵權 |
| **責任歸屬** | 模組上傳者 | 開發者 100% 法律責任 |
| **舉報機制** | Steam Overlay 指向頁面 | 玩家可舉報 illegal AI / 侵權 |
| **特殊禁止** | 預設不公開、需明確設可見度 | Live-Generated Adult Sexual Content 完全禁 |

---

## 8. manifest schema 補強建議（涵蓋版權 / AI）

```json
{
  "id": "haloflag/abandoned-mansion",
  "name": "廢棄洋房調查",
  "version": "1.0.0",
  "schemaVersion": 1,
  "author": "haloflag",

  "contentLicense": {
    "narrative": "original",
    "art": "commissioned",
    "audio": "royalty-free",
    "fonts": "noto-sans-cjk-ofl"
  },

  "aiContent": {
    "preGenerated": false,
    "preGeneratedDetails": null,
    "liveGenerated": false
  },

  "contentWarnings": ["psychological-horror", "violence"],
  "ageRating": "16+",

  "thirdPartyAttributions": [
    { "asset": "art/scenes/storm.png", "source": "Stock Asset by ArtistName", "license": "Royalty-Free Commercial" }
  ]
}
```

引擎驗證：
- 若 `preGenerated: true` → 必須在 details 描述用了什麼 AI 工具
- 若 `liveGenerated: true` → 玩家警告 + 描述 guardrails
- `thirdPartyAttributions` 自動生成 credits 畫面

---

## 9. 一句話總結

**Steam 把版權與 AI 揭露的法律責任完全推到開發者身上**，但提供 DMCA 與舉報機制讓侵權能被處理。

**對 CardNarrative 三大紅線**：
1. 🔴 **避開 AI 美術**（避免標籤 + 風險）
2. 🔴 **所有美術 / 音效要有授權證明**（Task 15/16 上架前確認）
3. 🔴 **Workshop 上傳必須同意 UGC + 內容聲明**（Phase D）

**對模組製作者**：純資料 modding（JSON 改改）幾乎無風險；但若帶素材就**自己擔責**。

---

## 10. 行動 checklist（時序）

### 短期（任何任務開工前確認）

- [ ] 確認 abandoned-mansion narrative 是否含 AI 生成段落
- [ ] 維護 `THIRD-PARTY-LICENSES.md`（記錄所有外部授權）
- [ ] 維護 `art/SOURCES.md`（記錄每張 PNG 來源）

### 中期（Task 15 美術替換）

- [ ] 美術來源決策（委託 / 購買 / 自畫，避免 AI）
- [ ] 每張新 PNG 紀錄授權證明
- [ ] 字型授權確認（商用 OK）

### 中期（Task 16 音效）

- [ ] 音效 / BGM 來源（royalty-free / 委託）
- [ ] 授權證明歸檔

### 長期（上架前）

- [ ] 完成 Steam Content Survey AI 揭露
- [ ] 設置 DMCA 聯絡窗口（公司 email + 法律代表）
- [ ] 完成內容評等（IARC）申請
- [ ] 商店頁素材授權確認

### Workshop 整合期（Phase D）

- [ ] UGC Agreement dialog
- [ ] AI 揭露勾選欄
- [ ] manifest aiContent / contentLicense 欄位 + 引擎驗證
- [ ] 模組選擇 UI AI / 版權標籤
- [ ] 舉報模組按鈕
