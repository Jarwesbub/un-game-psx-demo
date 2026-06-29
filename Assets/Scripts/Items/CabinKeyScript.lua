-- Script for Cabin Key ("key_0")

function onCreate(self)
    local hasKey = Persist.Get("has_key")
    -- Hide the key if its already picked.
    if hasKey == 1 then
        Entity.SetActive(self, false)
    end
end

function onInteract(self)
    Persist.Set("has_key", 1)
    Debug.Log("Player picked a key!")
    Entity.SetActive(self, false)
end
