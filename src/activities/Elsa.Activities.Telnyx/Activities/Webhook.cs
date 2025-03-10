﻿using Elsa.Activities.Telnyx.Extensions;
using Elsa.Activities.Telnyx.Webhooks.Models;
using Elsa.Activities.Telnyx.Webhooks.Payloads.Call;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Services;
using Elsa.Services.Models;

namespace Elsa.Activities.Telnyx.Activities
{
    public class Webhook : Activity
    {
        [ActivityOutput] public TelnyxWebhook? Model { get; set; }

        protected override IActivityExecutionResult OnExecute(ActivityExecutionContext context) => context.WorkflowExecutionContext.IsFirstPass ? ExecuteInternal(context) : Suspend();
        protected override IActivityExecutionResult OnResume(ActivityExecutionContext context) => ExecuteInternal(context);

        private IActivityExecutionResult ExecuteInternal(ActivityExecutionContext context)
        {
            var webhookModel = (TelnyxWebhook) context.Input!;

            if (webhookModel.Data.Payload is CallPayload callPayload)
            {
                context.WorkflowExecutionContext.CorrelationId ??= callPayload.CallSessionId;

                if (!context.HasCallControlId())
                    context.SetCallControlId(callPayload.CallControlId);

                if (callPayload is CallInitiatedPayload callInitiatedPayload)
                    if (!context.HasFromNumber())
                        context.SetFromNumber(callInitiatedPayload.To);
            }

            Model = webhookModel;
            return Done(webhookModel);
        }
    }
}