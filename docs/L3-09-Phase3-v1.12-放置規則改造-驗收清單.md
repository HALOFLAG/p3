# L3-09 Phase 3 v1.12 — 地塊放置規則改造 + 11×11 擴展 L2 驗收清單

> 對應規格書 v1.12（[`docs/遊戲規格書-v1.0.md`](./遊戲規格書-v1.0.md)）§1.5 / §3.1.4 / §6.5.6
> 完整設計分析：[`docs/分析備份/地塊放置規則-分析.md`](./分析備份/地塊放置規則-分析.md)
> Backend 已自動驗證：548 / 548 xUnit 全綠
> 本檔列出 **F5 必驗** 的手動互動項

## 啟動

1. 在 Godot Editor 開啟專案 → F5 啟動主場景
2. 模組自動載入 abandoned-mansion；初始狀態：玩家位於 11×11 grid 中央 (5,5) = village-square（村莊廣場）
3. RightPanel 顯示「下一張 / 第 2 張 / 第 3 張」slot（預設 deck preview）
4. TopBar 應有「抽地塊」按鈕（或既有觸發點）

---

## A. Grid 11×11 擴展視覺

| # | 步驟 | 預期結果 |
|---|---|---|
| A1 | 載入完成後觀察主地圖 | 中央 (5,5) 顯示 village-square（建築樣式）；上下左右各延伸 5 格 = 11×11 棋盤 |
| A2 | 視窗大小不變的情況下檢查棋盤可見範圍 | **預期前排（最近一列）兩側 col=±5 / 部分 ±4 的 tile 會被視窗左右邊緣截斷** — v1.13 已知議題（BaseTileSize 70 不變 + 11 cols 總寬 ~753 > ViewWidth 560）|
| A3 | 中鍵拖曳主地圖 | 視野可平移；放開後維持偏移；可滑到看不見的邊緣格 |
| A4 | 點小地圖右下「回到玩家中心」 | 主視野立即重置回玩家 (5,5) 為中心 |
| A5 | 觀察小地圖（右上 100×100） | 黃色 viewport 框覆蓋整個 11×11 grid（與主視野同步擴展） |
| A6 | 觀察玩家立繪（ParallaxScene） | 浮在 (5,5) 上方，位置正確且未被左右面板遮 |

---

## B. RightPanel 批次選擇 UI（Stage 5）

| # | 步驟 | 預期結果 |
|---|---|---|
| B1 | 點 TopBar「抽地塊」按鈕 | RightPanel 三 slot 從「下一張 / 第 2 張 / 第 3 張」切為「選擇 1 / 選擇 2 / 選擇 3」；游標滑入 slot 變指向手指 |
| B2 | abandoned-mansion 第 1 組 batch = `["village-store", "ruined-chapel"]`（2 張） | slot 1 / slot 2 顯示對應 tile（村內雜貨店 / 廢棄禮拜堂）；slot 3 空（虛線框） |
| B3 | 點 slot 1（village-store） | slot 1 標題變「持有」+ 金邊高亮；slot 2 / 3 不變；TURN LOG 顯示「持有：village-store」 |
| B4 | 滑鼠移到主地圖 | Ghost tile（半透明 PNG）跟隨游標；**滑到 RightPanel / 小地圖 / LeftPanel 仍能跟隨**（v1.12 修：ghost 寄生於頂層 CanvasLayer） |
| B5 | 點 slot 2 重新選（re-select） | held 換成 slot 2 的 tile；slot 1 變回未持有；slot 2 highlight；TURN LOG 顯示新 held |
| B6 | 持有時點 slot 1（自己的 slot） | No-op（state 不變、log 不寫；後端 SelectFromBatch 回 false） |
| B7 | 持有時按右鍵任意空白區 | 取消放置；held 退回原 visual slot（不退到末尾）；mode 回 Idle；TURN LOG 顯示「已取消放置」 |
| B8 | 取消後再次「抽地塊」 | 沿用既有批次（不重新抽）；slot 內容與取消前相同 |

---

## C. Tag 配對放置規則（Stage 4）

> 起點 village-square tags=`[village, outdoor]`

| # | 步驟 | 預期結果 |
|---|---|---|
| C1 | 抽地塊 → 第 1 組 batch（village-store / ruined-chapel） | 兩張都有 `village` tag → 與起點相容 |
| C2 | 持有 village-store，hover 鄰格 (4,5) / (5,4) / (6,5) / (5,6) | 主地圖該 4 格顯示綠色合法區 highlight |
| C3 | Click (5,6) | 放置成功；TURN LOG 顯示「放置地塊：Path 於 (5,6)」 |
| C4 | （後續批次抽到 underground-passage tags=`[underground]` 時）持有它 hover 起點旁 | 起點 (5,5)=outdoor 與 underground 無共享 tag → **無綠色合法區**（IsLegalPlacement 回 false） |
| C5 | 持有 mansion-foyer tags=`[outdoor, indoor]` hover 起點旁 | outdoor 與起點 outdoor 共享 → 合法（綠色 highlight）|

---

## D. 連續放置地塊組（Stage 6 / 7）

> 抽到第 5 組 = `["mansion-grand-foyer"]`（2×2 rectangle, indoor, 4 cells）

**前置**：先放完前 4 組 batch，讓 mansion-grand-foyer 出現。簡化測試法：開發版可在 prologue.json 把 mansion-grand-foyer 放在第 1 組。

| # | 步驟 | 預期結果 |
|---|---|---|
| D1 | 持有 mansion-grand-foyer 後查看 TURN LOG | 顯示「持有：mansion-grand-foyer（地塊組 1/4），請連續放 4 格。」 |
| D2 | 放第 1 格（指向某個 indoor 鄰格） | TURN LOG 顯示「地塊組進度 1/4 — 請繼續放下一格」；mode 仍 MapExpand；held 不清；ghost 仍跟游標 |
| D3 | 嘗試放遠處不相鄰格 | 拒絕；TURN LOG 顯示「(R,C) 不在組相鄰範圍；繼續從同組已放格旁挑格，或按右鍵取消整組。」 |
| D4 | 放第 2 格（與第 1 格相鄰） | 進度 2/4 |
| D5 | 嘗試放讓 bounding box 超過 2×2 的位置（如連續橫向 3 格） | 拒絕（GroupShape rectangle:2x2 強制） |
| D6 | 放第 3 格與第 4 格在合法的 2×2 box 內 | 進度 3/4 → 4/4；mode 自動回 Idle；held 清；4 格顯示**金色 3px 外框**包圍整個 2×2 區塊 |
| D7 | 在組進行中（如 2/4 階段）按右鍵取消 | 已放的 1/2 格從地圖移除；held 退回原 visual slot；mode 回 Idle；TURN LOG 顯示「已取消放置」 |

### D-line：grand-hallway（line:2，2 格直線）

| # | 步驟 | 預期結果 |
|---|---|---|
| DL1 | 抽到第 7 組（包含 grand-hallway）→ 持有 | TURN LOG 顯示「持有：grand-hallway（地塊組 1/2）」 |
| DL2 | 放第 1 格 → 第 2 格放橫向（同 row） | 接受；金色外框包圍 2 格直線 |
| DL3 | 重做：放第 1 格 → 第 2 格放縱向（同 col） | 接受；金色外框包圍 2 格直線 |
| DL4 | 放第 1 格 → 嘗試第 2 格放對角線 | 拒絕（adjacency 即可阻擋；不會走到 line:2 shape 檢查） |

---

## E. 區塊金色外框（Stage 7）

| # | 步驟 | 預期結果 |
|---|---|---|
| E1 | 完成放置 mansion-grand-foyer (2×2) | 4 格外圍呈現金色 3px 邊框；內部相鄰邊不畫線（避免雙線） |
| E2 | 完成放置 grand-hallway (line:2) | 2 格外圍呈金色邊框，2 格之間不畫內部分隔線 |
| E3 | 中鍵拖曳視野 / 攝影機補間 | 金色外框跟隨 tile quad 重新計算位置（透視變形正確） |
| E4 | 觀察 mansion-grand-foyer 與相鄰非組 tile | 兩者間有金色邊框（屬 mansion-grand-foyer 外圍） |
| E5 | 同一棋盤多個地塊組（如已放 mansion-grand-foyer 又放 grand-hallway） | 每組各自獨立金色外框，不會誤連 |

---

## F. 移動 tag 配對（Stage 8）

> 玩家在起點 (5,5) village-square=outdoor

| # | 步驟 | 預期結果 |
|---|---|---|
| F1 | 鋪設一條全 outdoor 路徑（forest-path → forest-causeway → mansion-front-yard） | 玩家可順著走，每格 hover 顯示綠色路徑 |
| F2 | 鋪設 underground-passage（underground）相鄰起點（無橋接） | 路徑無法穿越；hover underground tile 顯示「無路可達（需 tag 相容 / 經橋接 tile）」 |
| F3 | 在 outdoor 與 underground 之間放橋接 tile：mansion-foyer（outdoor+indoor）+ hidden-chamber（indoor+underground） | 路徑成功穿越；BFS 計算經 foyer → hidden-chamber 到達 underground 區 |
| F4 | 觀察 hover 路徑預覽 | 連線經過橋接 tile；節點顏色按 AP 段分綠/紅 |
| F5 | 嘗試 hover 不可達的 tag boundary 格（無橋接路徑） | 預覽 overlay 不顯示；click 該格 → AppendLog 「無路可達...」 |

---

## 已知議題（v1.12 不修，後續再處理）

1. **BaseTileSize 70 不變導致前排 tile 超出視窗**：11 cols × 0.978 scale × 70 ≈ 753px > ViewWidth 560；col=±5 / 部分 ±4 前排 tile 會被視窗左右邊緣截斷。後續調 BaseTileSize ~46 可恢復。
2. **y 軸略溢出**：11 rows × 0.775 scale × 70 ≈ 597 > ViewHeight 380 可用空間 → 最近一排略超出底部。同 R1，BaseTileSize 調整一併解決。
3. **abandoned-mansion fill ratio 從 27% 降到 18%（11×11 = 121 格，22 + 4 + 1 = 27 cells 佔用 + 起始 1 格 = 28 / 121）**：玩家可能感到走道空曠。後續可補幾張 outdoor / underground 單格 tile 提升至 ~25%。

---

## 驗收紀錄欄位

| 區塊 | 通過 / 問題描述 | 驗收日期 |
|---|---|---|
| A. Grid 擴展 |  |  |
| B. RightPanel UI |  |  |
| C. Tag 配對放置 |  |  |
| D. 連續放置組 |  |  |
| D-line 直線組 |  |  |
| E. 金色外框 |  |  |
| F. 移動 tag 配對 |  |  |

驗收人：__________
驗收日期：__________
