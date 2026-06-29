function onInteract(self)
    local hasKey = Persist.Get("has_key") or 0
    if hasKey == 1 then
        Debug.Log("Action: Gate opened")
        Scene.Load(3)
    else
        Debug.Log("Player has no key!")
    end
end
