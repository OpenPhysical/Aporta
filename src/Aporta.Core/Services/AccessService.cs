using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aporta.Core.DataAccess;
using Aporta.Core.DataAccess.Repositories;
using Aporta.Extensions.Hardware;
using Aporta.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Aporta.Core.Services
{
    public class AccessService
    {
        private readonly ExtensionService _extensionService;
        private readonly ILogger<AccessService> _logger;
        private readonly DoorRepository _doorRepository;
        private readonly EndpointRepository _endpointRepository;
        private readonly CredentialRepository _credentialRepository;

        public AccessService(IDataAccess dataAccess, ExtensionService extensionService, ILogger<AccessService> logger)
        {
            _extensionService = extensionService;
            _logger = logger;
            _doorRepository = new DoorRepository(dataAccess);
            _credentialRepository = new CredentialRepository(dataAccess);
            _endpointRepository = new EndpointRepository(dataAccess);
        }

        public void Startup()
        {
            _extensionService.AccessCredentialReceived += ExtensionServiceOnAccessCredentialReceived;
        }

        public void Shutdown()
        {
            _extensionService.AccessCredentialReceived -= ExtensionServiceOnAccessCredentialReceived;
        }

        private void ExtensionServiceOnAccessCredentialReceived(object sender,
            AccessCredentialReceivedEventArgs eventArgs)
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    var endpoints = (await _endpointRepository.GetAll()).ToArray();

                    var matchingDoor = await MatchingDoor(eventArgs.AccessPoint.Id, endpoints);

                    if (matchingDoor == null)
                    {
                        _logger.LogInformation(
                            "Credential received from {Name} was not assigned to a door", eventArgs.AccessPoint.Name);
                        return;
                    }

                    var matchingDoorStrike = MatchingDoorStrike(matchingDoor.DoorStrikeEndpointId, endpoints);

                    if (matchingDoorStrike == null)
                    {
                        _logger.LogInformation("Door {Name} didn't have a strike assigned", matchingDoor.Name);
                        return;
                    }

                    if (eventArgs.CardData.Count != eventArgs.BitCount)
                    {
                        _logger.LogInformation("Door {Name} card read doesn't match bit count", matchingDoor.Name);
                        return;
                    }

                    var builder = new StringBuilder();
                    foreach (bool bit in eventArgs.CardData)
                    {
                        builder.Append(bit ? "1" : "0");
                    }

                    var assignedCredential = await _credentialRepository.AssignedCredential(builder.ToString());
                    if (assignedCredential == null)
                    {
                        _logger.LogInformation("Door {Name} enrolled badge", matchingDoor.Name);
                        return;
                    }

                    if (!AccessGranted())
                    {
                        _logger.LogInformation("Door {Name} denied access", matchingDoor.Name);
                        return;
                    }

                    _logger.LogInformation("Door {Name} granted access", matchingDoor.Name);
                    await OpenDoor(matchingDoorStrike, 3);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unable to process access event");
                }
            });
        }

        private async Task OpenDoor(Endpoint matchingDoorStrike, int strikeTimer)
        {
            await _extensionService
                .GetControlPoint(matchingDoorStrike.ExtensionId, matchingDoorStrike.DriverEndpointId)
                .SetState(true);
            await Task.Delay(TimeSpan.FromSeconds(strikeTimer));
            await _extensionService
                .GetControlPoint(matchingDoorStrike.ExtensionId, matchingDoorStrike.DriverEndpointId)
                .SetState(false);
        }

        private bool AccessGranted()
        {
            return true;
        }

        private async Task<Door> MatchingDoor(string accessPointId, Endpoint[] endpoints)
        {
            var doors = await _doorRepository.GetAll();

            var matchingDoor = doors.FirstOrDefault(door =>
                MatchingEndpointId(endpoints, accessPointId, door.InAccessEndpointId) ||
                MatchingEndpointId(endpoints, accessPointId, door.OutAccessEndpointId));
            return matchingDoor;
        }
        
        private static bool MatchingEndpointId(IEnumerable<Endpoint> endpoints, string accessPointId, int? endpointId)
        {
            if (endpointId == null) return false;
            return endpointId == endpoints.FirstOrDefault(endpoint => endpoint.DriverEndpointId == accessPointId)?.Id;
        }

        private static Endpoint MatchingDoorStrike(int? doorStrikeEndpointId, IEnumerable<Endpoint> endpoints)
        {
            return endpoints.FirstOrDefault(endpoint => doorStrikeEndpointId == endpoint.Id);
        }
    }
}