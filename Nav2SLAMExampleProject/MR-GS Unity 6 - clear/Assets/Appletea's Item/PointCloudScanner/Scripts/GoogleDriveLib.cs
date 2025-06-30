using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using System;
using System.Net;


namespace Appletea.Dev.PointCloud
{
    public class GoogleDriveLib : MonoBehaviour
    {
        // Key
        private string JSON_FILE;
        // Google Drive Directory
        [SerializeField]
        private string GOOGLE_DRIVE_FOLDER_ID = "1tWhtxDBCYJfBwfclj7FX4OjDDnTB2g_Q";
        // Upload Filepath
        private string FILE_PATH;

        private DriveService _driveService;

        private void Start()
        {
            BetterStreamingAssets.Initialize();
            

            // �F�؏����擾
            GoogleCredential credential;
            using (var stream = BetterStreamingAssets.OpenRead("Authentication_private_key.json"))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(DriveService.ScopeConstants.Drive);
            }
            // Drive API�̃T�[�r�X���쐬
            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Depth Scanner Conector",
            });
        }


        public void UploadFileToDrive(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError("File not found at: " + filePath);
                return;
            }
            

            // �A�b�v���[�h����t�@�C���̃��^�f�[�^���쐬
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new[] { GOOGLE_DRIVE_FOLDER_ID },
            };


            Debug.Log("Upload start");//�����܂ł�OK
            Debug.Log("Service:" + _driveService.Name);

            var files = BetterStreamingAssets.GetFiles("\\", "*", SearchOption.AllDirectories);

            foreach (var _file in files)
            {
                Debug.Log(_file);
            }

            var request = _driveService.Files.Create(fileMetadata, new FileStream(filePath, FileMode.Open), "text/plain");
            Debug.Log("Files Created");
            request.Fields = "name, id";
            request.UploadAsync();
            Debug.Log("Upload Done!");
        }
    }
}