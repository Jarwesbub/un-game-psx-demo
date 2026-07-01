function onCreate()
    Persist.Set("came_from", 0)
    Persist.Set("has_key", 0) -- Reset cabin key pick.
    Camera.FollowPsxPlayer(false)
end

function onUpdate(self, dt)
    local camRot = Camera.GetRotation()
    local camRotStep = FixedPoint.new(1) / 526
    Camera.SetRotation(Vec3.new(camRot.x, camRot.y + camRotStep, camRot.z))
end

function onButtonPress(self, button)
    if button == Input.START then
        -- Start game.
        Scene.Load(1)
    end
end
