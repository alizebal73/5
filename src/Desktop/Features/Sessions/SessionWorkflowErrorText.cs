using System.Globalization;

namespace GameNet.Desktop.Features.Sessions;

internal static class SessionWorkflowErrorText
{
        public static string For(string code) => code switch
        {
            "security.https_required" => Text("برای اتصال به سرور راه دور HTTPS لازم است.", "HTTPS is required for a remote Server."),
            "tariffs.forbidden" or "sessions.forbidden" or "billing.forbidden" => Text("مجوز انجام این عملیات را ندارید.", "You do not have permission for this operation."),
            "tariffs.code_exists" => Text("کد این تعرفه قبلاً ثبت شده است.", "This tariff code already exists."),
            "sessions.station_unavailable" => Text("این ایستگاه آمادهٔ شروع Session نیست.", "This station is not available for a new Session."),
            "sessions.agent_unavailable" => Text("Agent این PC متصل یا دارای اعتبار/heartbeat معتبر نیست؛ اتصال Agent را بررسی کنید. Session شروع نشد.", "This PC's Agent is not connected or has no active credential and fresh heartbeat/lease. Check the Agent connection; the Session was not started."),
            "sessions.timing_recovery_required" or "sessions.invalid_clock" => Text("پیوستگی زمان جلسه قابل تأیید نیست؛ صورتحساب صادر نشد و بازیابی زمان لازم است.", "Session timing continuity could not be verified. No invoice was created; timing recovery is required."),
            "sessions.timing_recovery_invalid" => Text("مدت، علت یا تأیید بازیابی زمان معتبر نیست.", "The recovery duration, reason, or confirmation is invalid."),
            "sessions.timing_recovery_not_required" => Text("پیوستگی زمان معتبر است؛ از پایان عادی Session استفاده کنید.", "Timing continuity is valid; use the normal End Session command."),
            "sessions.station_has_active_session" => Text("برای این PC یک Session فعال ثبت شده است.", "This PC already has an active Session."),
            "sessions.station_pending_settlement" => Text("ابتدا صورتحساب Session قبلی را تسویه کنید.", "Pay the previous Session invoice before starting another Session on this PC."),
            "sessions.customer_blocked" => Text("این مشتری مسدود است و نمی‌تواند Session جدیدی شروع کند.", "This customer is blocked and cannot start a new Session."),
            "sessions.tariff_station_mismatch" => Text("این تعرفه برای PC نیست.", "This tariff is not for PCs."),
            "sessions.tariff_not_found" => Text("تعرفهٔ فعال پیدا نشد.", "The active tariff was not found."),
            "sessions.version_conflict" or "billing.session_version_conflict" => Text("اطلاعات تغییر کرده است؛ تازه‌سازی کنید.", "The data changed; refresh before trying again."),
            "billing.cash_insufficient" => Text("وجه دریافتی کمتر از صورتحساب است.", "Cash received is less than the invoice amount."),
            "billing.reversal_method_unsupported" => Text("این عملیات فقط برای پرداخت نقدی پشتیبانی می‌شود.", "This operation supports cash payments only."),
            "billing.reversal_invalid" => Text("علت یا اطلاعات برگشت معتبر نیست.", "The reversal reason or request is invalid."),
            "billing.reversal_confirmation_mismatch" => Text("صورتحساب، پرداخت یا مبلغ تأییدشده با اطلاعات سرور تطبیق ندارد.", "The confirmed invoice, payment, or amount does not match Server state."),
            "billing.tender_correction_reversal_forbidden" => Text("پس از برگشت فروش، اصلاح سوابق وجه نقد مجاز نیست.", "Cash tender facts cannot be corrected after a sale reversal."),
            "billing.tender_correction_version_conflict" or "billing.tender_correction_conflict" => Text("سوابق وجه نقد تغییر کرده‌اند؛ تازه‌سازی و بررسی دوباره لازم است.", "Cash tender facts changed; refresh and review the current values."),
            "billing.tender_correction_confirmation_mismatch" => Text("پرداخت انتخاب‌شده با صورتحساب تطبیق ندارد.", "The selected payment does not match the invoice."),
            "billing.tender_correction_invalid" or "billing.tender_correction_no_change" => Text("مبلغ یا علت اصلاح معتبر نیست، یا مقدار جدید با مقدار فعلی یکسان است.", "The corrected amount or reason is invalid, or the new amount matches the current value."),
            "billing.invoice_not_pending" => Text("این صورتحساب در انتظار پرداخت نیست.", "This invoice is not awaiting payment."),
            "idempotency.in_flight" => Text("این درخواست هنوز در حال پردازش است؛ کمی بعد وضعیت را تازه‌سازی کنید.", "This request is still processing; refresh its status before retrying."),
            "idempotency.key_reused" => Text("کلید تکرار درخواست با اطلاعات متفاوت استفاده شده است.", "The idempotency key was reused with different request data."),
            _ => Text("عملیات انجام نشد؛ پیام خطای پایدار ثبت شد. وضعیت را تازه‌سازی کنید.", "The operation failed. Refresh to reconcile the current state.")
        };

    private static string Text(string fa, string en) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;
}
