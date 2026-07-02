local isStarted = 0

function onCreate(self)
    isStarted = 0
end

function onInteract(self)
    -- Key is not picked.
    if isStarted == 0 then
        startDialogue({ "The moon is shining so bright.", "There's a chill in the wind." })
        isStarted = 1
    else
        advanceDialogue()
        -- Re-enable interaction when dialogue ends
        if not isInDialogue() then -- Dialogue ends.
            isStarted = 0
            Interact.SetEnabled(self, true)
        end
    end
end
