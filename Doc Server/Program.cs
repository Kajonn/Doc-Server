
using Doc_Server;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

Random random = new Random();

UploadManager uploadManager = new UploadManager(
    (UploadRequest request) => {
        const double chanceOfFailure = 0.05;
        const double maxUploadTimeS = 10.0;

        TimeSpan sleepTime;
        bool success;

        lock (random)
        {
            success = random.NextDouble() > chanceOfFailure;
            sleepTime = TimeSpan.FromSeconds(random.NextDouble() * maxUploadTimeS);
        }

        Thread.Sleep(sleepTime);

        UploadResult uploadResult = new UploadResult();
        if (success)
        {
            uploadResult.Result = UploadResult.ResultType.Completed;
            uploadResult.Message = "Successfully uploaded " + request.Path + " for user " + request.User;
        }
        else {
            uploadResult.Result = UploadResult.ResultType.Failed;
            uploadResult.Message = "Error when uploading " + request.Path + " for user " + request.User;
        }
        return uploadResult;
    },
    4,
    app.Services.GetRequiredService<ILogger<UploadManager>>());

//for (int i = 0; i < 10; i++) {
//    UploadRequest request = new();
//    request.Path = "file_" + i;
//    request.User = "user_" + i;
//    uploadManager.UploadFile(request);
//}


app.MapPost("/upload", (UploadRequest reguest) => {
    try
    {
        return uploadManager.UploadFile(reguest);
    }
    catch (Exception ex)
    {
        return new UploadIdentifier();
    }
} );

app.MapGet("/upload/{idstr}", (String idstr) =>
{
    try
    {
        var id = UInt64.Parse(idstr);
        var astatus = uploadManager.GetStatus(id);
        if(astatus.HasValue)
        {
            if( astatus.Value.Status == UploadStatus.StatusType.Completed ) 
            { //Remove completed when their statuses have been retrived
                uploadManager.RemoveStatus(id);
            }
        }
        return astatus;

    } catch(Exception e)
    {
        return null;
    }
});

app.Run();
