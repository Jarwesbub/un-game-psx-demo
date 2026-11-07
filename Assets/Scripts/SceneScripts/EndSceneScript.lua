local boundaryData = { [0] = { min = { x = -10, z = 18 }, max = { x = 10, z = 32 } }, }

function onCreate(self)
    Camera.FollowPsxPlayer(false)
    local pos = Vec3.new(0, 0, 0)
    local rot = Vec3.new(0, 0, 0)
    globalSetCameraPosition(pos, rot)
    setGlobalPlayerBoundaries(boundaryData[0])
    -- Play background music.
    Audio.PlayCDDA(5, 1)
end

function onButtonPress(self, button)
    if button == Input.SELECT then
        -- Return to main menu.
        Persist.Set("came_from", -1)
        Audio.PauseCDDA()
        Scene.Load(0)
    end
end
