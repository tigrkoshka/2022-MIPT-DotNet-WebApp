using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;

namespace Server.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private static readonly HashSet<string> SupportedExtensions = new() { ".png", ".jpg", ".jpeg" };
    private static readonly string SaveDir = "../tmp";

    /// <summary>
    /// Saves an image and puts it in queue for processing
    /// </summary>
    /// <param name="image"></param>
    /// <returns>The created file ID and the scheduled task ID</returns>
    /// <remarks>
    /// The supported formats are .png, .jpg and .jpeg
    /// </remarks>>
    /// <response code="201">The file is valid, file saved, task scheduled</response>
    /// <response code="400">The file has an invalid extension (only .png, .jpg and .jpeg are supported)</response>            
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(string))]
    public ActionResult<string> Upload([Required] IFormFile image)
    {
        var extension = Path.GetExtension(image.FileName);
        if (!SupportedExtensions.Contains(extension))
        {
            return BadRequest(
                $"Extension {extension} not supported. Expected one of {string.Join(" ", SupportedExtensions.ToList())}");
        }

        // generate guid
        var taskGuid = Guid.NewGuid().ToString();

        // save file
        var imagePath = Path.Combine(SaveDir, $"{taskGuid}{extension}");
        var statusPath = Path.Combine(SaveDir, $"{taskGuid}_status.txt");

        using (var stream = System.IO.File.Create(imagePath))
        {
            image.CopyTo(stream);
        }

        using (var fs = new FileStream(statusPath, FileMode.Create, FileAccess.Write))
        {
            fs.Write(Encoding.UTF8.GetBytes("Pending"));
        }

        // push filename to queue
        ConnectionFactory factory = new() { HostName = "localhost" };
        using (var connection = factory.CreateConnection())
        using (var channel = connection.CreateModel())
        {
            channel.QueueDeclare(queue: "task_queue",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            channel.BasicPublish(exchange: "",
                routingKey: "task_queue",
                basicProperties: null,
                body: Encoding.UTF8.GetBytes($"{taskGuid}{extension}"));
        }

        return taskGuid;
    }

    /// <summary>
    /// Gets a task's status
    /// </summary>
    /// <param name="taskGuid">The task's GUID (returned from upload)</param>
    /// <returns>The specified task's status</returns>
    /// <remarks>
    /// The status may be one of "Pending", "Processing", "Success", "Failure"
    /// </remarks>>
    /// <response code="200">The task was found, the status is returned</response>
    /// <response code="404">The task was not found</response>            
    [HttpPost("{taskGuid}/status")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> Status(string taskGuid)
    {
        var statusFilePath = Path.Combine(SaveDir, $"{taskGuid}_status.txt");
        if (!System.IO.File.Exists(statusFilePath))
        {
            return NotFound("No task with the specified GUID found");
        }
        
        return Ok(System.IO.File.ReadAllText(statusFilePath));
    }

    /// <summary>
    /// Gets a task's result (i.e. the caption generated for the respective image)
    /// </summary>
    /// <param name="taskGuid">The task's GUID (returned from upload)</param>
    /// <returns>The specified task's status</returns>
    /// <remarks>
    /// Precondition: /status returns "Success"
    /// </remarks>>
    /// <response code="200">The task was found, the status is returned</response>
    /// <response code="404">The task was not found</response>            
    [HttpPost("{taskGuid}/download")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> Download(string taskGuid)
    {
        var resultFilePath = Path.Combine(SaveDir, $"{taskGuid}_result.txt");
        if (!System.IO.File.Exists(resultFilePath))
        {
            return NotFound("Task with the specified GUID either does not exist or has resulted in an error");
        }

        return Ok(System.IO.File.ReadAllText(resultFilePath));
    }
    
    /// <summary>
    /// Gets the error occured while executing the task
    /// </summary>
    /// <param name="taskGuid">The task's GUID (returned from upload)</param>
    /// <returns>The specified task's status</returns>
    /// <remarks>
    /// Precondition: /status returns "Failure"
    /// </remarks>>
    /// <response code="200">The task was found, the error is returned</response>
    /// <response code="404">The task was not found</response>            
    [HttpPost("{taskGuid}/error")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> Error(string taskGuid)
    {
        var resultFilePath = Path.Combine(SaveDir, $"{taskGuid}_error.txt");
        if (!System.IO.File.Exists(resultFilePath))
        {
            return NotFound("Task with the specified GUID either does not exist or has finished successfully");
        }

        return Ok(System.IO.File.ReadAllText(resultFilePath));
    }
}