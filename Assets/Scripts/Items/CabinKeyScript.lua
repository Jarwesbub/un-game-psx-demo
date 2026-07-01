-- Script for Cabin Key ("key_0")
local isKeyPicked = 0

function onCreate(self)
    isKeyPicked = Persist.Get("has_key")
    -- Hide the key if its already picked.
    if isKeyPicked == 1 then
        Entity.SetActive(self, false) -- Hide key.
    end
end

function onInteract(self)
    -- Key is not picked.
    if isKeyPicked == 0 then
        Persist.Set("has_key", 1)
        isKeyPicked = 1
        Debug.Log("Player picked a key!")
        Interact.SetEnabled(self, true)
        startDialogue({ '"You found a key."' })
    else
        advanceDialogue()
        -- Re-enable interaction when dialogue ends
        if not isInDialogue() then
            Interact.SetEnabled(self, true)
            Entity.SetActive(self, false) -- Hide key.
        end
    end
end
