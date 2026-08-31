# 論文排版與撰寫規範

適用於 `docs/thesis/main/`（XeLaTeX，NTUST 範本 v1.7 改）。
**開始寫任何一節之前先讀這份**，避免各章格式不一致、後期回頭大改。

版面規格的來源有兩份：`Harry_M11301112_thesis_final_submission_FINAL.docx`（NTUST 送審版 Word 樣式＝字級與結構的權威）與 `Harrison_Thesis.pdf`（行距與留白的權威）。兩者已在 2026-08-10 實測並烘進 `my_ntust_thesis.tex`，**排版參數不要再逐處微調**，需要改就改那一個檔案。

---

## 0. 一句話版

寫內文時只碰 `sections/0X-*.tex`、`frontpages/my_*.tex`、`my_bib.bib` 三種檔案；**不要在章節檔裡下任何字體、字級、行距、間距指令**。引用一律 `\citep{key}` → 印成 `[1]`。

---

## 1. 檔案分工

| 你要做的事 | 改哪個檔 |
|---|---|
| 寫某一章的內文 | `sections/01-introduction.tex` … `06-conclusion.tex` |
| 寫中英文摘要、誌謝、縮寫表、符號表 | `frontpages/my_cabstract.tex` / `my_eabstract.tex` / `my_ackn.tex` / `my_abbreviations.tex` / `my_symbols.tex` |
| 加參考文獻 | `my_bib.bib`（同時更新 `../refer/index.md`） |
| 改版面／字級／目錄樣式 | **只改 `my_ntust_thesis.tex`** 的客製化區塊 |
| 封面欄位（題目、指導教授、日期） | `frontpages/my_names.tex` |
| 前置頁章名用語 | `english_trans.tex` |

**不要改**：`ntust_report.cls`、`common_env.tex`、`frontpages/ntust_frontpages.tex`、`backpages/ntust_backpages.tex`。
（例外：這三處已為本論文動過，記錄在此以免日後困惑——`common_env.tex` 停用了 `cite` 套件、`ntust_frontpages.tex` 重排了前置頁順序與頁碼起點、`ntust_backpages.tex` 改了書目樣式與字級。）

編譯：在 `main/` 下執行 `.\build.ps1`（xelatex → bibtex → xelatex ×2）。
`.\watch.ps1` 可存檔即重編。**編譯前先關掉 PDF 閱讀器**，否則檔案被鎖會直接失敗；若失敗過一次，先刪 `my_ntust_thesis.{toc,lof,lot,aux,out}` 再重編（半截的 `.toc` 會造成 `File ended while scanning use of \contentsline`）。

---

## 2. 版面與字級（已設定好，只供對照檢查）

| 項目 | 規格 |
|---|---|
| 紙張／邊界 | A4；上 3cm、下 2cm、左右各 3cm |
| 英文字體 | Times New Roman（中文一律標楷體，xeCJK 自動分派） |
| 正文 | 12pt，行距 1.5，首行縮排 21.6pt |
| 章標題 | 20pt 粗體置中，**`CHAPTER N` 一行、章名一行**，兩行皆全大寫，貼齊版面頂端 |
| 節標題 `1.1` | 18pt 粗體，靠左，編號與標題間**一個空格** |
| 小節標題 `1.1.1` | 14pt 粗體，靠左 |
| 頁碼 | 置中頁尾，12pt |
| 目錄／圖表目錄 | 12pt、行距 1.5、條目靠左、點引導至右側頁碼 |

頁碼制度：封面不印、推薦書與審定書不計頁；**摘要＝I**（大寫羅馬數字），一路到 NOMENCLATURE；**CHAPTER 1＝1**（阿拉伯數字）。

---

## 3. 標題怎麼寫

```latex
\chapter{Problem Description and Model Formulation}
\label{ch:model}

\section{Problem Description}
\label{sec:model-problem}

\subsection{System Setting and Operational Flow}
\label{sec:model-setting}
```

- **章名在 `.tex` 裡用字首大寫**（Title Case）即可，排版時會自動轉全大寫；目錄同步大寫。手動打全大寫會讓目錄與 `\ref` 變醜。
- 節／小節標題用 **Title Case**（實詞字首大寫，`of`／`and`／`the` 等虛詞小寫）。
- 最多用到 `\subsubsection`（1.1.1.1 不編號、也不進目錄），實務上盡量不要用到第四層。
- 每個 `\chapter`／`\section`／`\subsection` 後面**都要接 `\label`**，命名慣例：
  `ch:<章>`、`sec:<章>-<關鍵字>`（如 `sec:model-pricing`）。
- 交叉引用一律加不斷行空格：`Section~\ref{sec:model-pricing}`、`Chapter~\ref{ch:model}`。

---

## 4. 段落

- **直接打字就好**，第一段也會自動縮排；**不要自己加 `\indent`、`\hspace`、空白字元、`\\` 換行**。
- 段落之間空一行；**不要空兩行以上**（不會產生額外間距，只是雜訊）。
- 不要用 `\\` 斷行來排版；需要換行就是新段落。
- 中英混排時中文用標楷體、英文自動 Times New Roman，**不要手動切字體**。
- 一個段落講一件事，避免整節只有一段或一句話成段。

---

## 5. 圖

```latex
\begin{figure}[htbp]
  \centering
  \includegraphics[width=0.8\textwidth]{figures/layout-small.pdf}
  \caption{Layout of the small-scale instance.}
  \label{fig:layout-small}
\end{figure}
```

- 圖檔放 `main/figures/`，優先用向量（PDF/EPS）；點陣圖至少 300 dpi。
- **caption 放圖的下方**，句首大寫、句末加句點。
- `\label` 命名 `fig:<關鍵字>`，緊接在 `\caption` 之後（放前面會抓錯編號）。
- 內文一律用 `Figure~\ref{fig:xxx}` 指涉，**不要寫「下圖」「如上圖所示」**。
- 每張圖都必須在內文被引用至少一次，且引用出現在圖之前。
- 寬度用 `\textwidth` 的比例，不要寫死 `cm`。

## 6. 表

```latex
\begin{table}[htbp]
  \centering
  \caption{Throughput comparison under the small-scale instance.}
  \label{tab:small-throughput}
  \begin{tabular}{lrrr}
    \toprule
    Method & Orders & Items & EOR (kJ/item) \\
    \midrule
    M1G  & 1180 & 4482 & 12.31 \\
    M4G  & 1213 & 5169 & 11.02 \\
    \bottomrule
  \end{tabular}
\end{table}
```

- **caption 放表的上方**（與圖相反），`\label` 緊接其後。
- 用 `booktabs`（已載入）的 `\toprule`／`\midrule`／`\bottomrule`；**不要畫直線**，也不要用 `\hline`。
- 數值欄靠右對齊（`r`），文字欄靠左（`l`）。
- 同一表內小數位數一致；平均值與標準差寫成 `12.31 (0.42)` 並在表註說明。
- 顯著性檢定的 t 值／p 值放表註（`threeparttable` 已載入），不要塞進標題。
- 內文用 `Table~\ref{tab:xxx}` 指涉。

## 7. 式子

```latex
\begin{equation}
  \max \sum_{o \in O} \sum_{i \in I_o} \sum_{s \in S} \mu\,q_{ois}
  \label{eq:objective}
\end{equation}
```

- 需要被引用的式子用 `equation` 環境並加 `\label{eq:xxx}`；不需引用的用 `equation*`。
- 內文指涉寫 `Equation~(\ref{eq:objective})` 或 `(\ref{eq:objective})`。
- 式子是句子的一部分：前面接冒號或動詞，式尾按語法加逗號或句點。
- 符號一律用數學模式（`$\delta$`、`$q_{ois}$`），**不要在內文直接打 δ**；所有符號都要進 `frontpages/my_symbols.tex`。
- 集合用大寫（$O$、$S$）、索引用小寫（$o$、$s$）、參數用希臘字母，全篇一致。

---

## 8. 引用與參考文獻（APA 7 ＋ `[n]` 編號）

### 內文怎麼引

| 情境 | 寫法 | 印出來 |
|---|---|---|
| 一般引用 | `\citep{jiao2026online}` | `[1]` |
| 多篇 | `\citep{xie2021split,tadumadze2023assigning}` | `[2, 3]`（自動排序、連號會縮成 `[2-4]`） |
| 把作者當句子主詞 | `\citet{xie2021split}` | `Xie et al. [3]` |
| 指定頁碼 | `\citep[p.~85]{xie2021split}` | `[3, p. 85]` |

編號依**參考文獻清單的字母序**自動指派（清單本身按第一作者姓氏排序，符合 APA），所以**號碼會隨新增文獻而變動——絕對不要在內文手寫 `[1]`**。

### `my_bib.bib` 條目怎麼寫

```bibtex
@article{xie2021split,
  author  = {Xie, Lin and Thieme, Nils and Krenzler, Ruslan and Li, Hanyi},
  title   = {Introducing split orders and optimizing operational policies in robotic mobile fulfillment systems},
  journal = {European Journal of Operational Research},
  volume  = {288},
  number  = {1},
  pages   = {80--97},
  year    = {2021},
  doi     = {10.1016/j.ejor.2020.05.032}
}
```

規則：

- **key 命名**：`<第一作者姓氏><西元年><主題關鍵字>`，全小寫。
- `author` 用 `姓, 名 and 姓, 名` 的格式（**一定要有逗號分隔姓與名**，否則姓名縮寫會排錯）。中文作者也照 `Chuang, Kai-Hsiang` 寫。
- `title` 用 **sentence case**（只有首字與專有名詞大寫），這是 APA 的規定；專有名詞要用大括號保護，例如 `{G}urobi`、`{RMFS}`，否則有些樣式會強制轉小寫。
- `journal` 寫**期刊全名**、不縮寫（APA 要求），排版時自動轉斜體。
- `pages` 用兩個減號 `80--97`。
- 有 DOI 就填 `doi`（只填號碼、不要含 `https://doi.org/`），輸出會自動變成完整網址。
- 會議論文用 `@inproceedings` 且 `booktitle` 填會議全名；學位論文用 `@phdthesis`／`@mastersthesis`；書用 `@book`（要有 `publisher`）。

排版結果長這樣：

```
[3] Xie, L., Thieme, N., Krenzler, R., & Li, H. (2021). Introducing split orders and
    optimizing operational policies in robotic mobile fulfillment systems. European
    Journal of Operational Research, 288(1), 80–97.
```

### 流程

新增文獻時：把 PDF 放進 `docs/thesis/refer/`，在 `refer/index.md` 登記一列（檔名／key／出處／在論文中的角色），再補 `my_bib.bib`。**兩邊要同步**，不同步時無法判斷哪份是對的。

### 引用的既有教訓（來自 `refer/index.md`，寫作時務必遵守）

- **Xie = 拆單論文；Jiao = 線上聯合框架且不拆單，M1G 是 Jiao 的模型。** 曾經對調過一次，據此的推論全錯。
- **`online.pdf` 全篇未評估拆單**，不可引用為拆單證據。
- **不要引用「HADGS ≈ M1G 相差 1%」這個數值**——本研究實測差 9.38%，寫進去會與自己的表格矛盾。引論文只用來確立方法出處，**數字一律報自己的**。

---

## 9. 用字與敘述

- 全篇英文，**過去式寫做過的事**（實驗、模擬），**現在式寫模型性質與普遍事實**。
- 第一人稱用 "we"（或改被動），不要用 "I"。
- 縮寫**第一次出現時全稱在前、縮寫括號在後**：`robotic mobile fulfillment system (RMFS)`，之後一律用縮寫；同時要進 `my_abbreviations.tex`。
- 模型代號（M1G、M4G、HGS-M5、HADGS）全篇寫法固定，不要一下 M4G 一下 M4-G。
- 數字：小於 10 的整數在敘述中拼字（`three stations`），量測值一律用阿拉伯數字加單位（`45 bots`、`7200 s`）。
- 百分比變化要講清楚基準：`+15.4% in items handled relative to M1G`。
- 統計結果的口徑：**報顯著性就要附檢定統計量與 seed 數**（`+15.41%, t = 24.8, 5 seeds`）；不顯著就寫 "not significant"，不要用「略優」帶過。

---

## 10. 交稿前檢查

- [ ] `.\build.ps1` 無 `[X] 編譯錯誤`
- [ ] 無 `未定義的引用/參照`（`\ref`／`\cite` 打錯 key）
- [ ] `Overfull hbox` 逐一看過（多半是超長 URL 或表格過寬）
- [ ] 每張圖、每張表都在內文被引用過
- [ ] 目錄／圖目錄／表目錄頁碼正確（要跑滿四步編譯）
- [ ] 摘要頁是 I、CHAPTER 1 是 1
- [ ] `my_bib.bib` 與 `refer/index.md` 同步，無 TODO 條目殘留

---

## 11. 已知的小瑕疵（不必修）

- **書目 DOI 後面多一個句點**：APA 7 規定 DOI 網址後不加句點，但 BibTeX 的 `add.period$` 會補上。影響極小，若口試委員要求再處理。
- **目前有整頁空白**：章節骨架只有標題沒有內文，連續標題被視為不可分割的整塊而擠出空白頁（log 中的 `Overfull \vbox ... while \output is active`）。**填入段落文字後會自動消失**，不要為此調整版面參數。
