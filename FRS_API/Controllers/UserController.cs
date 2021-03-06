using FRS_API.Contracts;
using FRS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Speaker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FRS_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private IDBService dbService;
        string subscriptionKey = "b23d4db8df61461883a908492c8f238b";
        string region = "westus";
        string blobKey = "";
        string blobConnectionString = "DefaultEndpointsProtocol=https;AccountName=connecttechstorage;AccountKey=RkuHdqSRs9HECk+NN3mjXvBvRjFJ2dDpCT31+E/y6wweaS3u2ZcZOT4mInm8XfODC7FSSBDfb1PcPOCt6W4A2A==;EndpointSuffix=core.windows.net";
        string containerName = "innovate";
        public UserController(IDBService _dbService)
        {
            dbService = _dbService;
        }
        [HttpPost("")]
        public async Task<IActionResult> CreateUserAsync([FromBody] User user)
        {
            if (user == null || String.IsNullOrEmpty(user?.Name) || String.IsNullOrEmpty(user?.AzurePersonId))
            {
                return BadRequest("User cannot be null");
            }
            var newUser = await dbService.AddUserAsync(user);
            return Created("", newUser);
        }

        [HttpGet("")]
        public async Task<IActionResult> GetAllUsersAsync()
        {
            var users = await dbService.GetAllUsersAsync().ConfigureAwait(false);
            return Ok(users);
        }

        [HttpPost("UploadImage")]
        public async Task<IActionResult> UploadImage()
        {
            int rand = new Random().Next(0, 9999);
            var fs = new FileStream($"C:\\TempFace_{rand}.jpg", FileMode.OpenOrCreate);
            await Request.Body.CopyToAsync(fs);
            fs.Close();
            
            var response = await dbService.UploadBlobToContainer(containerName, $"{rand}_faceApiImage.jpg", $"C:\\TempFace_{rand}.jpg", blobConnectionString);
            return new JsonResult(response);
        }

        [HttpGet("Face")]
        public async Task<IActionResult> IsUserAuthenticated(int userId, string azurePersonId)
        {
            var authenticated = await dbService.IsUserAuthenticated(userId, azurePersonId).ConfigureAwait(false);
            if (authenticated)
            {
                return Ok();
            }
            else
            {
                return Unauthorized();
            }
        }

        [HttpPost("VoiceEnroll")]
        public async Task<IActionResult> EnrollVoice()
        {
            
            var config = SpeechConfig.FromSubscription(subscriptionKey, region);
            int rand = new Random().Next(0, 9999);
            // Save file to C:\\
            var fs = new FileStream($"C:\\TempWeb_Enroll_{rand}.wav",FileMode.OpenOrCreate);
            await Request.Body.CopyToAsync(fs).ConfigureAwait(false);
            fs.Close();

            var voiceId = await VerificationEnroll(config,rand);
            return new JsonResult(voiceId);
        }

        [HttpPost("Voice")]
        public async Task<IActionResult> AuthenticateVoice(int userId)
        {
            // TO-DO Save Audio received to C://Temp.wav 
            // Save file to C:\\
            int rand = new Random().Next(0, 9999);
            var fs = new FileStream($"C:\\TempWeb_{rand}.wav", FileMode.OpenOrCreate);
            await Request.Body.CopyToAsync(fs).ConfigureAwait(false);
            fs.Close();
            var config = SpeechConfig.FromSubscription(subscriptionKey, region);
            using (var client = new VoiceProfileClient(config))
            {
                var all = await client.GetAllProfilesAsync(VoiceProfileType.TextIndependentVerification);
                //TO-DO  Get Voice Profile basd on USerId from the above list
                 var voiceId = await dbService.GetUserVoiceId(userId).ConfigureAwait(false);
                 
                 var userProfile = all.Where(v => v.Id == voiceId).SingleOrDefault();

                var success = await SpeakerVerify(config, all[0],rand);
                if (success)
                    return Ok();
            }
            return Unauthorized();
        }

        public async Task<string> VerificationEnroll( SpeechConfig config, int randomId)
        {
            using (var client = new VoiceProfileClient(config))
            using (var profile = await client.CreateProfileAsync(VoiceProfileType.TextIndependentVerification, "en-us"))
            {

                using (var audioInput = AudioConfig.FromWavFileInput($"C:\\TempWeb_Enroll_{randomId}.wav"))
                {
                    Console.WriteLine($"Enrolling profile id {profile.Id}.");

                    VoiceProfileEnrollmentResult result = null;
                    while (result is null || result.RemainingEnrollmentsSpeechLength > TimeSpan.Zero)
                    {
                        Console.WriteLine("Continue speaking to add to the profile enrollment sample.");
                        result = await client.EnrollProfileAsync(profile, audioInput);
                        Console.WriteLine($"Remaining enrollment audio time needed: {result.RemainingEnrollmentsSpeechLength}");
                    }

                    if (result.Reason == ResultReason.EnrolledVoiceProfile)
                    {
                        Console.WriteLine("Enrolled with Id " + result.ProfileId);
                        // TO-DO Insert Data into Use table
                        return result.ProfileId;
                    }
                    else if (result.Reason == ResultReason.Canceled)
                    {
                        var cancellation = VoiceProfileEnrollmentCancellationDetails.FromResult(result);
                        Console.WriteLine($"CANCELED {profile.Id}: ErrorCode={cancellation.ErrorCode} ErrorDetails={cancellation.ErrorDetails}");
                        await client.DeleteProfileAsync(profile);
                    }
                    return "";
                }
            }

        }

        public static async Task<bool> SpeakerVerify(SpeechConfig config, VoiceProfile profile,int randomId)
        {
            try
            {
                var speakerRecognizer = new SpeakerRecognizer(config, AudioConfig.FromWavFileInput($"C://TempWeb_{randomId}.wav"));
                var model = SpeakerVerificationModel.FromProfile(profile);

                Console.WriteLine("Speak the passphrase to verify: \"My voice is my passport, please verify me.\"");
                var result = await speakerRecognizer.RecognizeOnceAsync(model);
                Console.WriteLine($"Verified voice profile for speaker : {result.ProfileId}, score is {result.Score} " +
                    $",Reason : {result.Reason},Id : {result.ResultId}");
                return result?.Score > 0.5;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;

            }
        }

    }
}
