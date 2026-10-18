# MRP Module Discovery

## Goal

MRP will eventually combine approved forecast, BOM, inventory, and procurement inputs to produce explainable shortage and purchase recommendations. No MRP business model or rule is implemented yet.

## Sheet-by-Sheet workflow

Legacy sheets are introduced one at a time as a language for explaining business intent. Each sheet must first be analyzed with `SHEET-ANALYSIS-TEMPLATE.md`. Implementation begins only after explicit project-owner approval for that sheet's scope.

Google Sheets is a read-only legacy reference unless the project owner explicitly authorizes a change. A sheet does not automatically become one database table or one web page. Business rules, data, UI behavior, reports, workarounds, and obsolete logic must be separated before software design.

## Correctness

Important calculations require an approved Golden Dataset containing known inputs, expected outputs, explanations where useful, and explicit decimal tolerances where applicable. New results must be compared with approved legacy scenarios; differences must be classified rather than hidden.

Missing rules that could affect MRP, inventory, purchasing, approval, finance, or data ownership must not be invented.

