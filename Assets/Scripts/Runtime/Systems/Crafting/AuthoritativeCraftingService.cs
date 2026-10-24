using System;
using System.Collections.Generic;

namespace ApirApir.Crafting
{
    /// <summary>
    /// Server-owned registry for active crafting controllers. Client identity is supplied
    /// by the transport layer and never accepted from request payloads.
    /// </summary>
    public sealed class AuthoritativeCraftingService
    {
        private readonly Dictionary<string, CraftingController> controllers;

        public AuthoritativeCraftingService()
        {
            controllers = new Dictionary<string, CraftingController>(StringComparer.Ordinal);
        }

        public event Action<CraftingEvent> EventPublished;

        public bool Register(CraftingController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            if (controllers.ContainsKey(controller.MatchId))
            {
                return false;
            }

            controllers.Add(controller.MatchId, controller);
            controller.EventPublished += ForwardEvent;
            return true;
        }

        public bool Unregister(string matchId)
        {
            if (!controllers.TryGetValue(matchId ?? string.Empty, out CraftingController controller))
            {
                return false;
            }

            controller.EventPublished -= ForwardEvent;
            controllers.Remove(matchId);
            return true;
        }

        public bool TryGetController(string matchId, out CraftingController controller)
        {
            return controllers.TryGetValue(matchId ?? string.Empty, out controller);
        }

        public CraftingResult Craft(CraftingRequest request, string authenticatedPlayerId)
        {
            if (!controllers.TryGetValue(request.MatchId, out CraftingController controller))
            {
                return CraftingResult.Rejected(
                    CraftingFailure.InvalidRequest,
                    authenticatedPlayerId,
                    request.RecipeId);
            }

            return controller.Craft(request, authenticatedPlayerId);
        }

        /// <summary>
        /// Entry point for authoritative minigame and battle reward systems.
        /// This method is intentionally not exposed as a client RPC.
        /// </summary>
        public MaterialRewardResult AwardMaterial(
            MaterialRewardRequest request,
            string playerId)
        {
            if (!controllers.TryGetValue(request.MatchId, out CraftingController controller))
            {
                return MaterialRewardResult.Rejected(
                    CraftingFailure.InvalidRequest,
                    request.MaterialId);
            }

            return controller.AwardMaterial(request, playerId);
        }

        private void ForwardEvent(CraftingEvent craftingEvent)
        {
            EventPublished?.Invoke(craftingEvent);
        }
    }
}
