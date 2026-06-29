function onInteract(self)
    local currentScene = Scene.GetIndex()

    Debug.Log("Action: Cabin door opened")
    Audio.Play("door_open_sound", 127, 64)

    if currentScene == 1 then -- Enter cabin.
        Persist.Set("came_from", 0)
        Scene.Load(2)
    elseif currentScene == 2 then -- Get out of cabin.
        Persist.Set("came_from", 1)
        Scene.Load(1)
    end
end
