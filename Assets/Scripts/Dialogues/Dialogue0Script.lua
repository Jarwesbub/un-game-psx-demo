local isStarted = 0

function onCreate(self)
    isStarted = 0
end

function onInteract(self)
    -- Key is not picked.
    if isStarted == 0 then
        startDialogue({ "I can't go back.", "I should keep going." })
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
