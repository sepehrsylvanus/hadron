using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Hadron.Erp.Api.Platform.Security;

public sealed record PermissionDefinition(
    string Code,
    string Area,
    string NameFa,
    string NameEn);

public static class SecurityClaimTypes
{
    public const string Permission = "hadron:permission";
    public const string PermissionDeny = "hadron:permission-deny";
    public const string AccessVersion = "hadron:access-version";
    public const string SessionId = "hadron:session-id";
    public const string MustChangePassword = "hadron:must-change-password";
    public const string FullName = "hadron:full-name";
    public const string TestAuthentication = "hadron:test-authentication";
}

public static class SystemRoleNames
{
    public const string SystemAdministrator = "SystemAdministrator";
}

public static class Permissions
{
    public const string IdentityUserView = "identity.user.view";
    public const string IdentityUserManage = "identity.user.manage";
    public const string IdentityPasswordReset = "identity.password.reset";
    public const string IdentityRoleView = "identity.role.view";
    public const string IdentityRoleManage = "identity.role.manage";
    public const string IdentityPermissionAssign = "identity.permission.assign";
    public const string IdentitySessionRevoke = "identity.session.revoke";
    public const string IdentitySecurityAuditView = "identity.security_audit.view";
    public const string IdentityProfileViewOwn = "identity.profile.view_own";
    public const string IdentityPasswordChangeOwn = "identity.password.change_own";
    public const string OrganizationUnitView = "organization.unit.view";
    public const string OrganizationUnitManage = "organization.unit.manage";
    public const string InternalPersonView = "organization.internal_person.view";
    public const string InternalPersonManage = "organization.internal_person.manage";
    public const string ReferenceDataView = "reference_data.view";
    public const string ReferenceDataManage = "reference_data.manage";
    public const string ProductView = "catalog.product.view";
    public const string ProductManage = "catalog.product.manage";
    public const string PartView = "catalog.part.view";
    public const string PartManage = "catalog.part.manage";
    public const string ComponentView = "catalog.component.view";
    public const string ComponentManage = "catalog.component.manage";
    public const string SupplierView = "procurement.supplier.view";
    public const string SupplierManage = "procurement.supplier.manage";
    public const string SourcingView = "procurement.sourcing.view";
    public const string SourcingManage = "procurement.sourcing.manage";
    public const string SourcingPreferredManage = "procurement.sourcing_preferred.manage";
    public const string SourcingLeadTimeApply = "procurement.sourcing_lead_time.apply";
    public const string BomView = "engineering.bom.view";
    public const string BomDraftManage = "engineering.bom_draft.manage";
    public const string BomApprove = "engineering.bom.approve";
    public const string BomObsolete = "engineering.bom.obsolete";
    public const string ForecastView = "planning.forecast.view";
    public const string ForecastManage = "planning.forecast.manage";
    public const string ActualView = "production.actual.view";
    public const string ActualManage = "production.actual.manage";
    public const string PeriodClose = "planning.period.close";
    public const string PeriodReopen = "planning.period.reopen";
    public const string InventoryView = "inventory.stock.view";
    public const string WarehouseManage = "inventory.warehouse.manage";
    public const string InventoryOpeningPost = "inventory.opening.post";
    public const string InventoryReceiptPost = "inventory.receipt.post";
    public const string InventoryIssuePost = "inventory.issue.post";
    public const string InventoryTransferPost = "inventory.transfer.post";
    public const string InventoryAdjustmentPost = "inventory.adjustment.post";
    public const string InventoryTransactionReverse = "inventory.transaction.reverse";
    public const string InventoryItemUnitManage = "inventory.item_unit.manage";
    public const string ProductionOrderView = "production.order.view";
    public const string ProductionOrderManage = "production.order.manage";
    public const string ProductionOrderApprove = "production.order.approve";
    public const string ProductionOrderRelease = "production.order.release";
    public const string ProductionMaterialStage = "production.material.stage";
    public const string ProductionReportPost = "production.report.post";
    public const string ProductionWorkCenterView = "production.work_center.view";
    public const string ProductionWorkCenterManage = "production.work_center.manage";
    public const string ProductionRoutingView = "production.routing.view";
    public const string ProductionRoutingManage = "production.routing.manage";
    public const string ProductionRoutingApprove = "production.routing.approve";
    public const string MrpRunView = "planning.mrp_run.view";
    public const string MrpRunExecute = "planning.mrp_run.execute";
    public const string MrpRouteManage = "planning.mrp_route.manage";
    public const string MaterialPlanningView = "planning.material.view";
    public const string PlanningExceptionView = "planning.exception.view";
    public const string PlannerWorkItemView = "planning.work_item.view";
    public const string PlannerWorkItemManage = "planning.work_item.manage";
    public const string PlannerWorkItemAcknowledge = "planning.work_item.acknowledge";
    public const string MaterialSavedViewManage = "planning.material_saved_view.manage";
    public const string WhatIfView = "planning.what_if.view";
    public const string WhatIfManage = "planning.what_if.manage";
    public const string OrderPlanningView = "procurement.order_planning.view";
    public const string OrderPlanningGenerate = "procurement.order_planning.generate";
    public const string OrderPlanningDecide = "procurement.order_planning.decide";
    public const string PurchaseOrderView = "procurement.purchase_order.view";
    public const string PurchaseOrderDraftManage = "procurement.purchase_order_draft.manage";
    public const string PurchaseOrderApprove = "procurement.purchase_order.approve";
    public const string PurchaseOrderReturnToDraft = "procurement.purchase_order.return_to_draft";
    public const string PurchaseOrderMarkOrdered = "procurement.purchase_order.mark_ordered";
    public const string PurchaseScheduleManage = "procurement.purchase_schedule.manage";
    public const string PurchaseMilestoneManage = "procurement.purchase_milestone.manage";
    public const string PurchaseScheduleCancel = "procurement.purchase_schedule.cancel";
    public const string PurchaseReceiptView = "procurement.purchase_receipt.view";
    public const string PurchaseReceiptCreate = "procurement.purchase_receipt.create";
    public const string PurchaseReceiptPost = "procurement.purchase_receipt.post";
    public const string PurchaseReceiptReverse = "procurement.purchase_receipt.reverse";
    public const string AnalyticsView = "analytics.view";
    public const string AnalyticsForecastAccuracyView = "analytics.forecast_accuracy.view";
    public const string AnalyticsLeadTimeView = "analytics.lead_time.view";
    public const string AnalyticsFinancialCommitmentView = "analytics.financial_commitment.view";
    public const string AnalyticsExport = "analytics.export";
    public const string CashFlowDashboardView = "cash_flow.dashboard.view";
    public const string CashFlowObligationView = "cash_flow.obligation.view";
    public const string CashFlowCostingView = "cash_flow.costing.view";
    public const string CashFlowPurchasePriceHistoryView = "cash_flow.purchase_price_history.view";
    public const string CashFlowReportingRateView = "cash_flow.reporting_rate.view";
    public const string CashFlowReportingRateManage = "cash_flow.reporting_rate.manage";
    public const string CashFlowMaterialCostBookView = "cash_flow.material_cost_book.view";
    public const string CashFlowMaterialCostBookManage = "cash_flow.material_cost_book.manage";
    public const string CashFlowMaterialCostBookPublish = "cash_flow.material_cost_book.publish";
    public const string TradingProjectView = "trading.project.view";
    public const string TradingProjectManage = "trading.project.manage";
    public const string TradingProjectClose = "trading.project.close";
    public const string TradingProjectReopen = "trading.project.reopen";
    public const string TradingReferenceManage = "trading.reference.manage";
    public const string TradingOrderRegistrationView = "trading.order_registration.view";
    public const string TradingOrderRegistrationManage = "trading.order_registration.manage";
    public const string TradingFxView = "trading.fx.view";
    public const string TradingFxManage = "trading.fx.manage";
    public const string TradingShipmentView = "trading.shipment.view";
    public const string TradingShipmentManage = "trading.shipment.manage";
    public const string TradingCustomsView = "trading.customs.view";
    public const string TradingCustomsManage = "trading.customs.manage";
    public const string TradingCostView = "trading.cost.view";
    public const string TradingCostManage = "trading.cost.manage";
    public const string TradingReportView = "trading.report.view";
    public const string TradingReportManage = "trading.report.manage";
    public const string TradingAuditView = "trading.audit.view";
    public const string CollaborationWorkspaceView = "collaboration.workspace.view";
    public const string CollaborationChannelManage = "collaboration.channel.manage";
    public const string CollaborationMessageSend = "collaboration.message.send";
    public const string CollaborationMessageModerate = "collaboration.message.moderate";
    public const string CollaborationFileShare = "collaboration.file.share";
    public const string CollaborationCallJoin = "collaboration.call.join";
    public const string CollaborationCallStart = "collaboration.call.start";
    public const string CollaborationMeetingView = "collaboration.meeting.view";
    public const string CollaborationMeetingSchedule = "collaboration.meeting.schedule";
    public const string CollaborationMeetingStart = "collaboration.meeting.start";
    public const string CollaborationMeetingJoin = "collaboration.meeting.join";
    public const string CollaborationMeetingManageParticipants = "collaboration.meeting.manage_participants";
    public const string CollaborationMeetingEnd = "collaboration.meeting.end";
    public const string CollaborationMeetingInviteGuest = "collaboration.meeting.invite_guest";
    public const string CollaborationMeetingReport = "collaboration.meeting.report";
    public const string CorrespondenceLetterView = "correspondence.letter.view";
    public const string CorrespondenceLetterManage = "correspondence.letter.manage";
    public const string CorrespondenceLetterIssue = "correspondence.letter.issue";
    public const string CorrespondenceLetterAcknowledge = "correspondence.letter.acknowledge";
}

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        D(Permissions.IdentityUserView, "IDENTITY", "مشاهده کاربران", "View users"),
        D(Permissions.IdentityUserManage, "IDENTITY", "مدیریت کاربران", "Manage users"),
        D(Permissions.IdentityPasswordReset, "IDENTITY", "بازنشانی رمز عبور", "Reset passwords"),
        D(Permissions.IdentityRoleView, "IDENTITY", "مشاهده نقش‌ها", "View roles"),
        D(Permissions.IdentityRoleManage, "IDENTITY", "مدیریت نقش‌ها", "Manage roles"),
        D(Permissions.IdentityPermissionAssign, "IDENTITY", "تخصیص دسترسی", "Assign permissions"),
        D(Permissions.IdentitySessionRevoke, "IDENTITY", "لغو نشست‌ها", "Revoke sessions"),
        D(Permissions.IdentitySecurityAuditView, "IDENTITY", "مشاهده تاریخچه امنیت", "View security audit"),
        D(Permissions.IdentityProfileViewOwn, "IDENTITY", "مشاهده پروفایل خود", "View own profile"),
        D(Permissions.IdentityPasswordChangeOwn, "IDENTITY", "تغییر رمز خود", "Change own password"),
        D(Permissions.OrganizationUnitView, "ORGANIZATION", "مشاهده واحدهای سازمانی", "View organizational units"),
        D(Permissions.OrganizationUnitManage, "ORGANIZATION", "مدیریت واحدهای سازمانی", "Manage organizational units"),
        D(Permissions.InternalPersonView, "ORGANIZATION", "مشاهده مالکان داخلی", "View internal people"),
        D(Permissions.InternalPersonManage, "ORGANIZATION", "مدیریت مالکان داخلی", "Manage internal people"),
        D(Permissions.ReferenceDataView, "MASTER_DATA", "مشاهده اطلاعات مرجع", "View reference data"),
        D(Permissions.ReferenceDataManage, "MASTER_DATA", "مدیریت اطلاعات مرجع", "Manage reference data"),
        D(Permissions.ProductView, "MASTER_DATA", "مشاهده محصولات", "View products"),
        D(Permissions.ProductManage, "MASTER_DATA", "مدیریت محصولات", "Manage products"),
        D(Permissions.PartView, "MASTER_DATA", "مشاهده قطعات", "View parts"),
        D(Permissions.PartManage, "MASTER_DATA", "مدیریت قطعات", "Manage parts"),
        D(Permissions.ComponentView, "MASTER_DATA", "مشاهده اجزا", "View components"),
        D(Permissions.ComponentManage, "MASTER_DATA", "مدیریت اجزا", "Manage components"),
        D(Permissions.SupplierView, "PROCUREMENT", "مشاهده تأمین‌کنندگان", "View suppliers"),
        D(Permissions.SupplierManage, "PROCUREMENT", "مدیریت تأمین‌کنندگان", "Manage suppliers"),
        D(Permissions.SourcingView, "PROCUREMENT", "مشاهده منابع تأمین", "View sourcing"),
        D(Permissions.SourcingManage, "PROCUREMENT", "مدیریت منابع تأمین", "Manage sourcing"),
        D(Permissions.SourcingPreferredManage, "PROCUREMENT", "مدیریت منبع ترجیحی", "Manage preferred sourcing"),
        D(Permissions.SourcingLeadTimeApply, "PROCUREMENT", "اعمال پیشنهاد زمان تأمین", "Apply lead-time recommendation"),
        D(Permissions.BomView, "ENGINEERING", "مشاهده BOM", "View BOM"),
        D(Permissions.BomDraftManage, "ENGINEERING", "مدیریت پیش‌نویس BOM", "Manage draft BOM"),
        D(Permissions.BomApprove, "ENGINEERING", "تأیید BOM", "Approve BOM"),
        D(Permissions.BomObsolete, "ENGINEERING", "منسوخ‌کردن BOM", "Obsolete BOM"),
        D(Permissions.ForecastView, "PLANNING", "مشاهده پیش‌بینی تولید", "View production forecast"),
        D(Permissions.ForecastManage, "PLANNING", "مدیریت پیش‌بینی تولید", "Manage production forecast"),
        D(Permissions.ActualView, "PRODUCTION", "مشاهده تولید واقعی", "View daily actual production"),
        D(Permissions.ActualManage, "PRODUCTION", "مدیریت تولید واقعی", "Manage daily actual production"),
        D(Permissions.PeriodClose, "PLANNING", "بستن دوره", "Close planning period"),
        D(Permissions.PeriodReopen, "PLANNING", "بازگشایی دوره", "Reopen planning period"),
        D(Permissions.InventoryView, "INVENTORY", "مشاهده موجودی", "View inventory"),
        D(Permissions.WarehouseManage, "INVENTORY", "مدیریت انبار", "Manage warehouses"),
        D(Permissions.InventoryOpeningPost, "INVENTORY", "ثبت موجودی افتتاحیه", "Post opening balance"),
        D(Permissions.InventoryReceiptPost, "INVENTORY", "ثبت رسید انبار", "Post inventory receipt"),
        D(Permissions.InventoryIssuePost, "INVENTORY", "ثبت خروج انبار", "Post inventory issue"),
        D(Permissions.InventoryTransferPost, "INVENTORY", "ثبت انتقال انبار", "Post inventory transfer"),
        D(Permissions.InventoryAdjustmentPost, "INVENTORY", "ثبت تعدیل موجودی", "Post inventory adjustment"),
        D(Permissions.InventoryTransactionReverse, "INVENTORY", "برگشت تراکنش موجودی", "Reverse inventory transaction"),
        D(Permissions.InventoryItemUnitManage, "INVENTORY", "مدیریت واحدهای تراکنشی قلم", "Manage item transaction units"),
        D(Permissions.ProductionOrderView, "PRODUCTION", "مشاهده دستورهای تولید", "View production orders"),
        D(Permissions.ProductionOrderManage, "PRODUCTION", "مدیریت پیش‌نویس دستور تولید", "Manage draft production orders"),
        D(Permissions.ProductionOrderApprove, "PRODUCTION", "تأیید دستور تولید", "Approve production orders"),
        D(Permissions.ProductionOrderRelease, "PRODUCTION", "آزادسازی دستور تولید", "Release production orders"),
        D(Permissions.ProductionMaterialStage, "PRODUCTION", "آماده‌سازی مواد تولید", "Stage production materials"),
        D(Permissions.ProductionReportPost, "PRODUCTION", "ثبت گزارش و مصرف تولید", "Post production reports"),
        D(Permissions.ProductionWorkCenterView, "PRODUCTION", "مشاهده مراکز کاری", "View work centers"),
        D(Permissions.ProductionWorkCenterManage, "PRODUCTION", "مدیریت مراکز کاری", "Manage work centers"),
        D(Permissions.ProductionRoutingView, "PRODUCTION", "مشاهده مسیرهای ساخت", "View manufacturing routes"),
        D(Permissions.ProductionRoutingManage, "PRODUCTION", "مدیریت پیش‌نویس مسیر ساخت", "Manage draft manufacturing routes"),
        D(Permissions.ProductionRoutingApprove, "PRODUCTION", "تأیید مسیر ساخت", "Approve manufacturing routes"),
        D(Permissions.MrpRunView, "PLANNING", "مشاهده اجرای MRP", "View MRP runs"),
        D(Permissions.MrpRunExecute, "PLANNING", "اجرای MRP", "Execute MRP"),
        D(Permissions.MrpRouteManage, "PLANNING", "تصمیم مسیر HYBRID", "Manage MRP route decisions"),
        D(Permissions.MaterialPlanningView, "PLANNING", "مشاهده برنامه‌ریزی مواد", "View material planning"),
        D(Permissions.PlanningExceptionView, "PLANNING", "مشاهده استثناهای برنامه‌ریزی", "View planning exceptions"),
        D(Permissions.PlannerWorkItemView, "PLANNING", "مشاهده کارتابل برنامه‌ریز", "View planner inbox"),
        D(Permissions.PlannerWorkItemManage, "PLANNING", "مدیریت کارتابل برنامه‌ریز", "Manage planner inbox"),
        D(Permissions.PlannerWorkItemAcknowledge, "PLANNING", "اعلام بررسی کار برنامه‌ریز", "Acknowledge planner work items"),
        D(Permissions.MaterialSavedViewManage, "PLANNING", "مدیریت نماهای ذخیره‌شده برنامه‌ریزی", "Manage material planning saved views"),
        D(Permissions.WhatIfView, "PLANNING", "مشاهده سناریوهای چه می‌شود اگر", "View what-if scenarios"),
        D(Permissions.WhatIfManage, "PLANNING", "محاسبه سناریوهای چه می‌شود اگر", "Calculate what-if scenarios"),
        D(Permissions.OrderPlanningView, "PROCUREMENT", "مشاهده برنامه سفارش", "View order planning"),
        D(Permissions.OrderPlanningGenerate, "PROCUREMENT", "تولید پیشنهاد سفارش", "Generate order recommendations"),
        D(Permissions.OrderPlanningDecide, "PROCUREMENT", "ثبت تصمیم کارشناسی", "Make expert decisions"),
        D(Permissions.PurchaseOrderView, "PROCUREMENT", "مشاهده سفارش خرید", "View purchase orders"),
        D(Permissions.PurchaseOrderDraftManage, "PROCUREMENT", "مدیریت پیش‌نویس سفارش خرید", "Manage draft purchase orders"),
        D(Permissions.PurchaseOrderApprove, "PROCUREMENT", "تأیید سفارش خرید", "Approve purchase orders"),
        D(Permissions.PurchaseOrderReturnToDraft, "PROCUREMENT", "بازگرداندن سفارش به پیش‌نویس", "Return purchase order to draft"),
        D(Permissions.PurchaseOrderMarkOrdered, "PROCUREMENT", "ثبت تعهد سفارش", "Mark purchase order ordered"),
        D(Permissions.PurchaseScheduleManage, "PROCUREMENT", "مدیریت برنامه تحویل", "Manage delivery schedules"),
        D(Permissions.PurchaseMilestoneManage, "PROCUREMENT", "مدیریت رویدادهای لجستیک", "Manage logistics milestones"),
        D(Permissions.PurchaseScheduleCancel, "PROCUREMENT", "لغو مانده برنامه تحویل", "Cancel schedule remainder"),
        D(Permissions.PurchaseReceiptView, "PROCUREMENT", "مشاهده رسید خرید", "View purchase receipts"),
        D(Permissions.PurchaseReceiptCreate, "PROCUREMENT", "ایجاد رسید خرید", "Create purchase receipts"),
        D(Permissions.PurchaseReceiptPost, "PROCUREMENT", "ثبت نهایی رسید خرید", "Post purchase receipts"),
        D(Permissions.PurchaseReceiptReverse, "PROCUREMENT", "برگشت رسید خرید", "Reverse purchase receipts"),
        D(Permissions.AnalyticsView, "ANALYTICS", "مشاهده تحلیل‌ها", "View analytics"),
        D(Permissions.AnalyticsForecastAccuracyView, "ANALYTICS", "مشاهده دقت پیش‌بینی", "View forecast accuracy"),
        D(Permissions.AnalyticsLeadTimeView, "ANALYTICS", "مشاهده تحلیل زمان تأمین", "View lead-time analytics"),
        D(Permissions.AnalyticsFinancialCommitmentView, "ANALYTICS", "مشاهده تعهدات تجاری", "View financial commitments"),
        D(Permissions.AnalyticsExport, "ANALYTICS", "خروجی گزارش", "Export reports"),
        D(Permissions.CashFlowDashboardView, "CASH_FLOW", "مشاهده داشبورد جریان نقدی", "View cash-flow dashboard"),
        D(Permissions.CashFlowObligationView, "CASH_FLOW", "مشاهده تعهدات پرداخت", "View payment obligations"),
        D(Permissions.CashFlowCostingView, "CASH_FLOW", "مشاهده بهای BOM", "View costed BOMs"),
        D(Permissions.CashFlowPurchasePriceHistoryView, "CASH_FLOW", "مشاهده تاریخچه قیمت خرید", "View purchase price history"),
        D(Permissions.CashFlowReportingRateView, "CASH_FLOW", "مشاهده نرخ‌های گزارشگری", "View reporting FX rates"),
        D(Permissions.CashFlowReportingRateManage, "CASH_FLOW", "مدیریت نرخ‌های گزارشگری", "Manage reporting FX rates"),
        D(Permissions.CashFlowMaterialCostBookView, "CASH_FLOW", "مشاهده دفتر بهای مواد", "View material cost books"),
        D(Permissions.CashFlowMaterialCostBookManage, "CASH_FLOW", "مدیریت پیش‌نویس دفتر بهای مواد", "Manage draft material cost books"),
        D(Permissions.CashFlowMaterialCostBookPublish, "CASH_FLOW", "انتشار دفتر بهای مواد", "Publish material cost books"),
        D(Permissions.TradingProjectView, "TRADING", "مشاهده پرونده‌های بازرگانی", "View trade projects"),
        D(Permissions.TradingProjectManage, "TRADING", "مدیریت پرونده‌های بازرگانی", "Manage trade projects"),
        D(Permissions.TradingProjectClose, "TRADING", "بستن یا تغییر وضعیت پرونده بازرگانی", "Close or transition trade projects"),
        D(Permissions.TradingProjectReopen, "TRADING", "بازگشایی پرونده بازرگانی", "Reopen trade projects"),
        D(Permissions.TradingReferenceManage, "TRADING", "مدیریت مراجع اختصاصی بازرگانی", "Manage trading reference values"),
        D(Permissions.TradingOrderRegistrationView, "TRADING", "مشاهده ثبت سفارش بازرگانی", "View trade order registration"),
        D(Permissions.TradingOrderRegistrationManage, "TRADING", "مدیریت ثبت سفارش بازرگانی", "Manage trade order registration"),
        D(Permissions.TradingFxView, "TRADING", "مشاهده فرایند ارزی", "View trade FX process"),
        D(Permissions.TradingFxManage, "TRADING", "مدیریت فرایند ارزی", "Manage trade FX process"),
        D(Permissions.TradingShipmentView, "TRADING", "مشاهده محموله و مسیر حمل", "View shipments and legs"),
        D(Permissions.TradingShipmentManage, "TRADING", "مدیریت محموله و مسیر حمل", "Manage shipments and legs"),
        D(Permissions.TradingCustomsView, "TRADING", "مشاهده ترخیص گمرکی", "View customs clearance"),
        D(Permissions.TradingCustomsManage, "TRADING", "مدیریت ترخیص گمرکی", "Manage customs clearance"),
        D(Permissions.TradingCostView, "TRADING", "مشاهده هزینه‌های بازرگانی", "View trade costs"),
        D(Permissions.TradingCostManage, "TRADING", "مدیریت هزینه‌های بازرگانی", "Manage trade costs"),
        D(Permissions.TradingReportView, "TRADING", "مشاهده گزارش‌های بازرگانی", "View trading reports"),
        D(Permissions.TradingReportManage, "TRADING", "مدیریت یادداشت‌های گزارش بازرگانی", "Manage trading report notes"),
        D(Permissions.TradingAuditView, "TRADING", "مشاهده تاریخچه بازرگانی", "View trading audit"),
        D(Permissions.CollaborationWorkspaceView, "COLLABORATION", "ورود به فضای ارتباطات", "Enter collaboration workspace"),
        D(Permissions.CollaborationChannelManage, "COLLABORATION", "مدیریت کانال‌ها", "Manage channels"),
        D(Permissions.CollaborationMessageSend, "COLLABORATION", "ارسال و ویرایش پیام خود", "Send and edit own messages"),
        D(Permissions.CollaborationMessageModerate, "COLLABORATION", "نظارت بر پیام‌ها", "Moderate messages"),
        D(Permissions.CollaborationFileShare, "COLLABORATION", "ارسال فایل", "Share files"),
        D(Permissions.CollaborationCallJoin, "COLLABORATION", "پیوستن به تماس", "Join calls"),
        D(Permissions.CollaborationCallStart, "COLLABORATION", "شروع و پایان تماس", "Start and end calls"),
        D(Permissions.CollaborationMeetingView, "COLLABORATION", "مشاهده جلسه‌ها", "View meetings"),
        D(Permissions.CollaborationMeetingSchedule, "COLLABORATION", "زمان‌بندی جلسه", "Schedule meetings"),
        D(Permissions.CollaborationMeetingStart, "COLLABORATION", "شروع جلسه", "Start meetings"),
        D(Permissions.CollaborationMeetingJoin, "COLLABORATION", "پیوستن به جلسه", "Join meetings"),
        D(Permissions.CollaborationMeetingManageParticipants, "COLLABORATION", "مدیریت شرکت‌کنندگان جلسه", "Manage meeting participants"),
        D(Permissions.CollaborationMeetingEnd, "COLLABORATION", "پایان‌دادن جلسه", "End meetings"),
        D(Permissions.CollaborationMeetingInviteGuest, "COLLABORATION", "دعوت مهمان به جلسه", "Invite meeting guests"),
        D(Permissions.CollaborationMeetingReport, "COLLABORATION", "مشاهده گزارش جلسه‌ها", "View meeting reports"),
        D(Permissions.CorrespondenceLetterView, "CORRESPONDENCE", "مشاهده نامه‌های مجاز", "View permitted letters"),
        D(Permissions.CorrespondenceLetterManage, "CORRESPONDENCE", "مدیریت پیش‌نویس نامه", "Manage draft letters"),
        D(Permissions.CorrespondenceLetterIssue, "CORRESPONDENCE", "صدور نامه رسمی", "Issue official letters"),
        D(Permissions.CorrespondenceLetterAcknowledge, "CORRESPONDENCE", "اعلام دریافت نامه", "Acknowledge letters")
    ];

    public static IReadOnlySet<string> Codes { get; } = All.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);

    private static PermissionDefinition D(string code, string area, string fa, string en) => new(code, area, fa, en);
}

public sealed record RequiredPermissionMetadata(string Code);

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!PermissionCatalog.Codes.Contains(permission))
            throw new InvalidOperationException($"Unknown permission '{permission}'.");
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new AuthorizeAttribute(permission));
            endpointBuilder.Metadata.Add(new RequiredPermissionMetadata(permission));
        });
        return builder;
    }
}
