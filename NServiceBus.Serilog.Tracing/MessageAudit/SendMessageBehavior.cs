using System;
using System.Threading.Tasks;
using NServiceBus.Pipeline;

namespace NServiceBus.Serilog.Tracing
{
    // wrap DispatchMessageToTransportBehavior
    class SendMessageBehavior : Behavior<IOutgoingLogicalMessageContext>
    {
        LogBuilder logBuilder;

        public SendMessageBehavior(LogBuilder logBuilder)
        {
            this.logBuilder = logBuilder;
        }

        public override async Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            var logger = logBuilder.GetLogger("NServiceBus.Serilog.MessageSent");
            var forContext = logger
                .ForContext("Message", context.Message.Instance, true)
                .ForContext("MessageType", context.Message.MessageType.ToString())
                .ForContext("MessageId", context.MessageId);
            forContext = forContext.AddHeaders(context.Headers);

            forContext.Information("Sent message {MessageType} {MessageId}");
            await next().ConfigureAwait(false);
        }

        public class Registration : RegisterStep
        {
            public Registration()
                : base("SerilogSendMessage", typeof(SendMessageBehavior), "Logs outgoing messages")
            {
                //InsertAfter(WellKnownStep.DispatchMessageToTransport);
            }
        }
    }
}

