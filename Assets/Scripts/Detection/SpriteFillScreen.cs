using UnityEngine;

namespace CardsVR.Detection
{
    public class SpriteFillScreen : MonoBehaviour
    {
        void Start()
        {
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;

            Vector3 norm = sr.sprite.bounds.size.normalized;
            transform.localScale = norm;
        }

    }
}
