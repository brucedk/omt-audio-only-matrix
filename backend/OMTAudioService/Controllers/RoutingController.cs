using Microsoft.AspNetCore.Mvc;
using OMTAudioService.Services;
using NAudio.CoreAudioApi;

namespace OMTAudioService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutingController : ControllerBase
{
    private readonly OMTRoutingService _routingService;
    private readonly OMTBroadcastService _broadcastService;

    public RoutingController(OMTRoutingService routingService, OMTBroadcastService broadcastService)
    {
        _routingService = routingService;
        _broadcastService = broadcastService;
    }

    [HttpGet("senders")]
    public ActionResult<string[]> GetSenders()
    {
        return Ok(_routingService.GetDiscoveredSenders());
    }

    [HttpGet("outputs")]
    public ActionResult GetOutputs()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var result = new List<object>();
        foreach (var device in devices)
        {
            result.Add(new { id = device.ID, name = device.FriendlyName });
        }
        return Ok(result);
    }

    [HttpGet("inputs")]
    public ActionResult GetInputs()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        var result = new List<object>();
        foreach (var device in devices)
        {
            result.Add(new { id = device.ID, name = device.FriendlyName });
        }
        return Ok(result);
    }

    [HttpGet("config")]
    public ActionResult<Dictionary<string, string>> GetConfig()
    {
        return Ok(_routingService.GetRouting());
    }

    [HttpPost("config")]
    public ActionResult UpdateConfig([FromBody] Dictionary<string, string> config)
    {
        _routingService.UpdateRouting(config);
        return Ok(new { success = true });
    }

    [HttpGet("broadcast-config")]
    public ActionResult<Dictionary<string, string>> GetBroadcastConfig()
    {
        return Ok(_broadcastService.GetBroadcasts());
    }

    [HttpPost("broadcast-config")]
    public ActionResult UpdateBroadcastConfig([FromBody] Dictionary<string, string> config)
    {
        _broadcastService.UpdateBroadcasts(config);
        return Ok(new { success = true });
    }
}
