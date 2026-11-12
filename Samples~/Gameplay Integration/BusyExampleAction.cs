using EasyInteraction;

namespace EasyLocalization.Integrations
{
    public sealed class BusyExampleAction : InteractionAction
    {
        public override InteractionAvailability GetAvailability(InteractionActor actor) =>
            InteractionAvailability.Unavailable(InteractionFailure.Busy,"Busy","interaction.Busy");
        protected override bool OnExecute(InteractionActor actor) => false;
    }
}
