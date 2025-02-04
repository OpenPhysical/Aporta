using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Aporta.Core.Services;
using Aporta.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aporta.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExtensionsController : ControllerBase
{
    private readonly ILogger<ExtensionsController> _logger;
    private readonly ExtensionService _extensionService;

    public ExtensionsController(ExtensionService extensionService,
        ILogger<ExtensionsController> logger)
    {
        _logger = logger;
        _extensionService = extensionService;
    }

    [HttpGet]
    public IEnumerable<Extension> Get()
    {
        return _extensionService.GetExtensions();
    }
        
    [HttpGet("{extensionId:Guid}")]
    public Extension Get(Guid extensionId)
    {
        return _extensionService.GetExtension(extensionId);
    }

    [HttpPost("{extensionId:Guid}")]
    public async Task<ActionResult> SetEnabled(Guid extensionId, [FromQuery] bool enabled)
    {
        try
        {
            await _extensionService.EnableExtension(extensionId, enabled);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to update extension {ExtensionId}", extensionId);
            return Problem(exception.Message);
        }

        return NoContent();
    }

    [HttpPost("{extensionId:Guid}/action/{actionType}")]
    public async Task<ActionResult> PerformAction(Guid extensionId, string actionType, [FromBody] dynamic parameter)
    {
        bool success = true;
        string result = string.Empty;
        try
        {
            result = await _extensionService.PerformAction(extensionId, actionType,
                JsonSerializer.Serialize(parameter));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to perform action {ActionType} for extension {ExtensionId}", actionType, extensionId);
            success = false;
        }

        return success ? Content(result) : Problem();
    }
}