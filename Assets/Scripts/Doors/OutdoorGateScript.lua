local isKeyPicked = 0

function onCreate(self)
    isKeyPicked = Persist.Get("has_key") or 0
end

function onInteract(self)
    if not isInDialogue() then
        if isKeyPicked == -1 then
            -- Key is already used.
            Scene.Load(3)
            return
        end
        playerStartDialogue()
    else
        playerAdvanceDialogue()
    end
end

function playerStartDialogue()
    if isKeyPicked == 0 then
        Debug.Log("Player has no key!")
        Interact.SetEnabled(self, true)
        startDialogue({ '"The door is locked."', "I need a key to open it." })
    else
        Debug.Log("Player has a key!")
        Interact.SetEnabled(self, true)
        isKeyPicked = -1 -- Key is used to the door.
        Persist.Set("has_key", -1)
        startDialogue({ '"You unlocked the door."' })
    end
end

function playerAdvanceDialogue()
    advanceDialogue()
    if not isInDialogue() then -- Dialogue ends.
        Interact.SetEnabled(self, true)
    end
end
