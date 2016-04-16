using System;
using System.Threading.Tasks;
using NServiceBus.Pipeline;
using NServiceBus.Sagas;
using Serilog;
using Serilog.Events;

namespace NServiceBus.Serilog.Tracing
{

    class CaptureSagaStateBehavior : Behavior<IInvokeHandlerContext>
    {
        SagaUpdatedMessage sagaAudit;
        ILogger logger;

        public CaptureSagaStateBehavior(LogBuilder logBuilder)
        {
            logger = logBuilder.GetLogger("NServiceBus.Serilog.SagaAudit");
        }

        public override async Task Invoke(IInvokeHandlerContext context, Func<Task> next)
        {
            ActiveSagaInstance activeSagaInstance;

            if (!context.Extensions.TryGet(out activeSagaInstance))
            {
                await next().ConfigureAwait(false);
                return; // Message was not handled by the saga
            }

            if (!logger.IsEnabled(LogEventLevel.Information))
            {
                await next().ConfigureAwait(false);
                return;
            }
            sagaAudit = new SagaUpdatedMessage
            {
                StartTime = DateTime.UtcNow,
                SagaType = activeSagaInstance.Instance.GetType().FullName
            };
            context.Extensions.Set(sagaAudit);
            await next().ConfigureAwait(false);

            sagaAudit.FinishTime = DateTime.UtcNow;
            AuditSaga(activeSagaInstance, context);
        }

        void AuditSaga(ActiveSagaInstance activeSagaInstance, IInvokeHandlerContext context)
        {
            string messageId;
            var saga = activeSagaInstance.Instance;
            if (!context.Headers.TryGetValue(Headers.MessageId, out messageId))
            {
                return;
            }

            var headers = context.Headers;
            var originatingMachine = headers["NServiceBus.OriginatingMachine"];
            var originatingEndpoint = headers[Headers.OriginatingEndpoint];
            var intent = context.MessageIntent();

            var initiator = new SagaChangeInitiator
            {
                IsSagaTimeoutMessage = context.IsTimeoutMessage(),
                InitiatingMessageId = messageId,
                OriginatingMachine = originatingMachine,
                OriginatingEndpoint = originatingEndpoint,
                MessageType = context.MessageMetadata.MessageType.FullName,
                TimeSent = context.TimeSent(),
                Intent = intent
            };
            sagaAudit.IsNew = activeSagaInstance.IsNew;
            sagaAudit.IsCompleted = saga.Completed;
            sagaAudit.SagaId = saga.Entity.Id;

            AssignSagaStateChangeCausedByMessage(context);

            logger
                .ForContext("SagaId", sagaAudit.SagaId)
                .ForContext("SagaType", sagaAudit.SagaType)
                .ForContext("StartTime", sagaAudit.StartTime)
                .ForContext("FinishTime", sagaAudit.FinishTime)
                .ForContext("IsCompleted", sagaAudit.IsCompleted)
                .ForContext("IsNew", sagaAudit.IsNew)
                .ForContext("Initiator", initiator, true)
                .ForContext("ResultingMessages", sagaAudit.ResultingMessages, true)
                .ForContext("SagaState", saga.Entity, true)
                .Information("Saga execution {SagaType} {SagaId}");
        }


        void AssignSagaStateChangeCausedByMessage(IInvokeHandlerContext context)
        {
            string sagaStateChange;
            if (!context.Headers.TryGetValue("NServiceBus.Serilog.Tracing.SagaStateChange", out sagaStateChange))
            {
                sagaStateChange = string.Empty;
            }

            var statechange = "Updated";
            if (sagaAudit.IsNew)
            {
                statechange = "New";
            }
            if (sagaAudit.IsCompleted)
            {
                statechange = "Completed";
            }

            if (!string.IsNullOrEmpty(sagaStateChange))
            {
                sagaStateChange += ";";
            }
            sagaStateChange += $"{sagaAudit.SagaId}:{statechange}";

            context.Headers["NServiceBus.Serilog.Tracing.SagaStateChange"] = sagaStateChange;
        }


        public class Registration : RegisterStep
        {
            public Registration()
                : base("SerilogCaptureSagaState", typeof(CaptureSagaStateBehavior), "Records saga state changes")
            {
                InsertBefore(WellKnownStep.InvokeSaga);
            }
        }
    }

}