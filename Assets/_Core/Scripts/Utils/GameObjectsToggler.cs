using UnityEngine;

public class GameObjectsToggler : MonoBehaviour
{
    public void GameObjectToggle(GameObject tmpGameObject)
    {
        tmpGameObject.SetActive(!tmpGameObject.activeSelf);
    }
}
