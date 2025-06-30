using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace Appletea.Dev
{
    public class DebugLog : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI debugTextUI;  // �f�o�b�O����\������Text�R���|�[�l���g
        private Queue logMessages = new Queue();  // ���O���b�Z�[�W�̃L���[
        private string currentLog = "";  // ���ݕ\������Ă��郍�O

        void OnEnable()
        {
            // ���O���b�Z�[�W�����b�X������
            Application.logMessageReceived += HandleLog;
        }

        void OnDisable()
        {
            // ���b�X�����~����
            Application.logMessageReceived -= HandleLog;
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            // ���O���L���[�ɒǉ�
            logMessages.Enqueue(logString);
            if (logMessages.Count > 10)  // �\�����郍�O�̍s���𐧌�
            {
                logMessages.Dequeue();
            }

            // �L���[���烍�O���b�Z�[�W�𕶎���Ƃ��đg�ݗ��Ă�
            currentLog = "";
            foreach (string log in logMessages)
            {
                currentLog += log + "\n";
            }
        }

        void Update()
        {
            // �e�L�X�gUI�Ƀ��O��\��
            if (debugTextUI != null)
            {
                debugTextUI.text = currentLog;
            }
        }
    }

}