using System.IO.Compression;
using System.Text;
using System.Xml;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;

namespace Hadron.Erp.Api.Modules.Analytics.Application;

public sealed class AnalyticsExportService(AnalyticsService analytics, MaterialPlanningService materialPlanning,
    OrderPlanningService orderPlanning, TimeProvider timeProvider)
{
    public async Task<AnalyticsFile> Export(AnalyticsExportRequest request, CancellationToken ct)
    {
        if (request.MaximumRows is < 1 or > AnalyticsService.MaximumExportRows)
            throw new AnalyticsValidationException(new Dictionary<string, string[]>
            { ["maximumRows"] = [$"Export limit must be 1..{AnalyticsService.MaximumExportRows}."] });
        if (request.Language is not ("fa-IR" or "en"))
            throw new AnalyticsValidationException(new Dictionary<string, string[]> { ["language"] = ["Unsupported language."] });

        var rows = new List<IReadOnlyList<string?>>();
        switch (request.Report)
        {
            case "mrp-run-summary":
                {
                    rows.Add(["RunNumber", "AsOf", "HorizonEnd", "EngineVersion", "Status", "Completeness",
                        "MaterialCount", "ExceptionCount", "CompletedAtUtc"]);
                    var header = await ResolveRun(request.RunId, ct);
                    rows.Add([header.RunNumber, header.AsOfDate.ToString("O"), header.HorizonEndDate.ToString("O"),
                        header.EngineVersion, header.Status, header.Completeness, header.MaterialCount.ToString(),
                        header.ExceptionCount.ToString(), header.CompletedAtUtc.ToString("O")]);
                    break;
                }
            case "material-risk":
                {
                    rows.Add(["RunNumber", "ItemCode", "Risk", "Completeness", "StartingOnHand", "FirmIncoming",
                        "NetRequirement", "StockoutDate", "SafetyBreachDate", "StockCoverage", "FirmCoverage"]);
                    var selected = await ResolveRun(request.RunId, ct);
                    var first = await MaterialRisk(selected.Id, request.Search, 1, ct);
                    EnsureBounded(first.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(first.TotalCount); page++)
                    {
                        var current = page == 1 ? first : await MaterialRisk(selected.Id, request.Search, page, ct);
                        foreach (var x in current.Items) rows.Add([selected.RunNumber, x.Item.Code, x.RiskBand,
                            x.Completeness, x.StartingOnHand.ToString(), x.TotalFirmIncoming.ToString(),
                            x.TotalNetRequirement.ToString(), x.StockoutDate?.ToString("O"),
                            x.SafetyBreachDate?.ToString("O"), x.StockCoverageDays?.ToString(),
                            x.FirmCoverageDays?.ToString()]);
                    }
                    break;
                }
            case "order-planning":
                {
                    rows.Add(["RunNumber", "ItemCode", "Priority", "NeedBy", "OrderBy", "MachineQty",
                        "MachineSource", "Decision", "FinalSupplier", "EstimatedValue", "Currency", "State"]);
                    var first = await OrderPlanning(request, 1, ct);
                    EnsureBounded(first.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(first.TotalCount); page++)
                    {
                        var current = page == 1 ? first : await OrderPlanning(request, page, ct);
                        foreach (var x in current.Items) rows.Add([x.RunNumber, x.ItemCode, x.Priority,
                            x.NeedByDate.ToString("O"), x.OrderByDate?.ToString("O"),
                            x.SystemRecommendedQty?.ToString(), x.SourcingNumber, x.LatestDecisionType,
                            x.SupplierNumber, x.EstimatedValue?.ToString(), x.CurrencyCode, x.State]);
                    }
                    break;
                }
            case "lead-time":
                {
                    rows.Add(["Sourcing", "ItemCode", "Supplier", "SampleCount", "Mean", "P50", "P80", "P90"]);
                    var result = await analytics.LeadTime(request.EvaluationDate, request.DataCutoffUtc, request.Search, 1,
                        Math.Min(request.MaximumRows, AnalyticsService.MaximumPageSize), ct);
                    EnsureBounded(result.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(result.TotalCount); page++)
                    {
                        var current = page == 1 ? result : await analytics.LeadTime(request.EvaluationDate,
                            request.DataCutoffUtc, request.Search, page, AnalyticsService.MaximumPageSize, ct);
                        foreach (var x in current.Items) rows.Add([x.SourcingNumber, x.ItemCode, x.SupplierNumber,
                        x.Total?.SampleCount.ToString(), x.Total?.Mean.ToString(), x.Total?.P50.ToString(),
                        x.Total?.P80.ToString(), x.Total?.P90.ToString()]);
                    }
                    break;
                }
            case "forecast-accuracy":
                {
                    rows.Add(["ProductCode", "Period", "Baseline", "Final", "Actual", "BaselineAPE", "FinalAPE"]);
                    var result = await analytics.ForecastAccuracy(request.DataCutoffUtc, request.Search, 1,
                        Math.Min(request.MaximumRows, AnalyticsService.MaximumPageSize), ct);
                    EnsureBounded(result.Page.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(result.Page.TotalCount); page++)
                    {
                        var current = page == 1 ? result : await analytics.ForecastAccuracy(request.DataCutoffUtc,
                            request.Search, page, AnalyticsService.MaximumPageSize, ct);
                        foreach (var x in current.Page.Items) rows.Add([x.ProductCode, $"{x.JalaliYear}/{x.JalaliMonth:00}",
                        x.BaselineForecast?.ToString(), x.FinalForecast?.ToString(), x.Actual.ToString(),
                        x.BaselineApe?.ToString(), x.FinalApe?.ToString()]);
                    }
                    break;
                }
            case "commercial-commitments":
            case "purchase-order-receipt":
                {
                    rows.Add(["PONumber", "Supplier", "Currency", "OriginalAmount", "OrderedQty", "ReceivedQty",
                        "CancelledQty", "OpenQty", "OriginalETA", "CurrentETA", "FirstReceipt", "FinalReceipt",
                        "Outcome", "Anchor", "AnchorDate", "ExpectedCreditDate"]);
                    var result = await analytics.Commercial(request.EvaluationDate, request.DataCutoffUtc, request.Search, 1,
                        Math.Min(request.MaximumRows, AnalyticsService.MaximumPageSize), ct);
                    EnsureBounded(result.Page.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(result.Page.TotalCount); page++)
                    {
                        var current = page == 1 ? result : await analytics.Commercial(request.EvaluationDate,
                            request.DataCutoffUtc, request.Search, page, AnalyticsService.MaximumPageSize, ct);
                        foreach (var x in current.Page.Items) rows.Add([x.PurchaseOrderNumber, x.SupplierNumber,
                        x.CurrencyCode, x.OriginalAmount.ToString(), x.OrderedQuantity.ToString(),
                        x.ReceivedQuantity.ToString(), x.CancelledQuantity.ToString(), x.OpenQuantity.ToString(),
                        x.OriginalEta?.ToString("O"), x.CurrentEta?.ToString("O"), x.FirstReceiptDate?.ToString("O"),
                        x.FinalReceiptDate?.ToString("O"), x.CompletionOutcome, x.CreditAnchor,
                        x.CreditAnchorDate?.ToString("O"), x.ExpectedCreditDate?.ToString("O")]);
                    }
                    break;
                }
            case "inventory-position":
                {
                    rows.Add(["ItemCode", "WarehouseCode", "UnitCode", "WarehouseOnHand", "CompanyOnHand",
                        "InactiveWarehouseWarning"]);
                    var result = await analytics.InventoryPositions(request.EvaluationDate, request.DataCutoffUtc,
                        request.Search, 1,
                        Math.Min(request.MaximumRows, AnalyticsService.MaximumPageSize), ct);
                    EnsureBounded(result.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(result.TotalCount); page++)
                    {
                        var current = page == 1 ? result : await analytics.InventoryPositions(request.EvaluationDate,
                            request.DataCutoffUtc, request.Search, page, AnalyticsService.MaximumPageSize, ct);
                        foreach (var x in current.Items) rows.Add([x.ItemCode, x.WarehouseCode, x.UnitCode,
                        x.OnHand.ToString(), x.CompanyOnHand.ToString(), x.InactiveWarehouseWarning.ToString()]);
                    }
                    break;
                }
            case "inventory-movement":
                {
                    rows.Add(["TransactionId", "Type", "EffectiveDate", "ItemCode", "WarehouseCode",
                        "QuantityEffect", "SourceDocumentType", "SourceDocumentId", "CreatedAtUtc"]);
                    var fromDate = request.FromDate ?? request.EvaluationDate.AddMonths(-1);
                    var toDate = request.ToDate ?? request.EvaluationDate;
                    var result = await analytics.InventoryMovements(fromDate, toDate, request.DataCutoffUtc,
                        request.Search, 1,
                        Math.Min(request.MaximumRows, AnalyticsService.MaximumPageSize), ct);
                    EnsureBounded(result.TotalCount, request.MaximumRows);
                    for (var page = 1; page <= Pages(result.TotalCount); page++)
                    {
                        var current = page == 1 ? result : await analytics.InventoryMovements(fromDate,
                            toDate, request.DataCutoffUtc, request.Search, page, AnalyticsService.MaximumPageSize, ct);
                        foreach (var x in current.Items) rows.Add([x.TransactionId.ToString(), x.TransactionType,
                            x.EffectiveDate.ToString("O"), x.ItemCode, x.WarehouseCode, x.Quantity.ToString(),
                            x.SourceDocumentType, x.SourceDocumentId?.ToString(), x.CreatedAtUtc.ToString("O")]);
                    }
                    break;
                }
            default:
                throw new AnalyticsValidationException(new Dictionary<string, string[]>
                { ["report"] = ["Unsupported report."] });
        }

        var generated = timeProvider.GetUtcNow().UtcDateTime;
        rows.Insert(0, ["Hadron ERP System", request.Report, $"evaluationDate={request.EvaluationDate:O}",
            $"dataCutoffUtc={(request.DataCutoffUtc ?? generated):O}", $"search={request.Search ?? string.Empty}",
            $"runId={request.RunId?.ToString() ?? string.Empty}",
            $"generationId={request.GenerationId?.ToString() ?? string.Empty}",
            $"fromDate={request.FromDate?.ToString("O") ?? string.Empty}",
            $"toDate={request.ToDate?.ToString("O") ?? string.Empty}", $"generatedAtUtc={generated:O}"]);
        if (request.Format != "xlsx")
            throw new AnalyticsValidationException(new Dictionary<string, string[]>
            { ["format"] = ["Only server-side XLSX is enabled; no canonical HTML/CSS PDF renderer is configured."] });
        return new(CreateXlsx(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"hadron-{request.Report}-{request.EvaluationDate:yyyyMMdd}.xlsx");
    }

    private static void EnsureBounded(int total, int maximum)
    {
        if (total > maximum) throw new AnalyticsConflictException("EXPORT_LIMIT_EXCEEDED",
            $"Filtered dataset contains {total} records; maximum export is {maximum}.");
    }
    private static int Pages(int total) => Math.Max(1, (int)Math.Ceiling(total / (decimal)AnalyticsService.MaximumPageSize));

    private static byte[] CreateXlsx(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(archive, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Write(archive, "_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(archive, "xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Hadron Analytics\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var r = 0; r < rows.Count; r++)
            {
                sheet.Append("<row r=\"").Append(r + 1).Append("\">");
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var value = XmlEscape(rows[r][c] ?? string.Empty);
                    sheet.Append("<c r=\"").Append(Column(c)).Append(r + 1).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                        .Append(value).Append("</t></is></c>");
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            Write(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return stream.ToArray();
    }

    private Task<MaterialPlanningSummaryListResponse> MaterialRisk(Guid runId, string? search, int page,
        CancellationToken ct) =>
        materialPlanning.Summary(runId, search, null, null, null, null, null, null, null, null,
            "risk", "asc", page, AnalyticsService.MaximumPageSize, ct);

    private Task<OrderRecommendationListResponse> OrderPlanning(AnalyticsExportRequest request, int page,
        CancellationToken ct) =>
        orderPlanning.List(request.RunId, request.GenerationId, request.Search, null, null, null, null, null, null, null,
            page, AnalyticsService.MaximumPageSize, ct);

    private async Task<MaterialPlanningRunHeader> ResolveRun(Guid? runId, CancellationToken ct)
    {
        var selectedId = runId;
        if (!selectedId.HasValue)
            selectedId = (await materialPlanning.Runs(1, 1, ct)).Items.SingleOrDefault()?.Id;
        if (!selectedId.HasValue)
            throw new AnalyticsConflictException("NO_COMPLETED_MRP_RUN", "No completed MRP Run is available.");
        return await materialPlanning.Header(selectedId.Value, ct)
            ?? throw new AnalyticsConflictException("MRP_RUN_UNAVAILABLE", "Selected MRP Run is unavailable.");
    }

    private static string XmlEscape(string value) => SecurityElementEscape(value);
    private static string SecurityElementEscape(string value)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true });
        writer.WriteString(value); writer.Flush(); return builder.ToString();
    }
    private static string Column(int index)
    {
        var result = string.Empty;
        do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
        return result;
    }
    private static void Write(ZipArchive archive, string path, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
