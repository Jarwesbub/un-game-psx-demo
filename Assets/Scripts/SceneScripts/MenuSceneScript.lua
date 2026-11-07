local cameraData = {
    [0] = { pos = { 0, 0, 0 }, rot = { 0, 0, 0 } },
    [1] = { pos = { -57, 10, 0 }, rot = { 12, -50, 0 } },
}

function onCreate(self)
    if Persist.Get("came_from") == -1 then
        setCameraPositionByIndex(1) -- Player beat the game.
    else
        setCameraPositionByIndex(0)
    end

    Persist.Set("came_from", 0)
    Persist.Set("has_key", 0) -- Reset cabin key pick.
    Camera.FollowPsxPlayer(false)
    -- Play background music.
    Audio.PlayCDDA(2)
end

function onUpdate(self, dt)
    local camRot = Camera.GetRotation()
    local camRotStep = FixedPoint.new(1) / 526
    Camera.SetRotation(Vec3.new(camRot.x, camRot.y + camRotStep, camRot.z))
end

function onButtonPress(self, button)
    if button == Input.START then
        -- Start game.
        Audio.PauseCDDA()
        Scene.Load(1)
    end
end

function setCameraPositionByIndex(index)
    local pos = Vec3.new(cameraData[index].pos[1], cameraData[index].pos[2], cameraData[index].pos[3])
    local rot = Vec3.new(cameraData[index].rot[1], cameraData[index].rot[2], cameraData[index].rot[3])
    --Debug.Log("Cam pos: " .. pos.x .. "," .. pos.y .. "," .. pos.z)
    globalSetCameraPosition(pos, rot)
end
