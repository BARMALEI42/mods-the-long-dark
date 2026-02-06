using UnityEngine;
using Il2Cpp;
using MelonLoader;

namespace TLD_Multiplayer
{
    public class RemotePlayer : MonoBehaviour
    {
        public RemotePlayer(System.IntPtr ptr) : base(ptr) { }

        private GameObject _visual;
        private Vector3 _nPos;
        private Quaternion _nRot;
        private bool _isReady = false;
        private bool _Crouch;

        public void Init() => Object.DontDestroyOnLoad(this.gameObject);

        private void AssembleCapsule()
        {
            if (_isReady || Il2Cpp.GameManager.IsMainMenuActive() || Il2Cpp.GameManager.GetPlayerObject() == null) return;

            // Создаем капсулу
            _visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _visual.transform.SetParent(this.transform, false);
            _visual.transform.localScale = new Vector3(1f, 1.8f, 1f);

            // Удаляем коллайдер (нужна ссылка на PhysicsModule в .csproj)
            var col = _visual.GetComponent<CapsuleCollider>();
            if (col != null) Object.Destroy(col);

            // Красим в синий
            var renderer = _visual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material.shader = Shader.Find("Standard");
                renderer.material.color = Color.blue;
            }

            _visual.layer = 0;
            _isReady = true;
            //MelonLogger.Msg("[RemotePlayer] Капсула создана. Текстуры дома должны быть на месте.");
        }

        public void UpdateSync(Vector3 p, float r,bool crouch)
        {
            _nPos = p;
            _nRot = Quaternion.Euler(0, r, 0);
            _Crouch = crouch;
        }

        void Update()
        {
            if (!_isReady) { AssembleCapsule(); return; }
            transform.position = Vector3.Lerp(transform.position, _nPos, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _nRot, Time.deltaTime * 10f);
            // ВЫЧИСЛЯЕМ НОВУЮ ВЫСОТУ:
            // Если игрок присел (_Crouch == true), высота 1.2 метра. 
            // Если стоит — 1.8 метра.
    float targetHeight = _Crouch ? 1.2f : 1.8f;

            // ПРИМЕНЯЕМ МАСШТАБ (Scale):
            // Мы плавно меняем размер капсулы через Lerp
            Vector3 targetScale = new Vector3(1f, targetHeight, 1f);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 5f);
        }
    }
}