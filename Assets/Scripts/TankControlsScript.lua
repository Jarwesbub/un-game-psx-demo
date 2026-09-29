-- Math calculations:
local zero = FixedPoint.new(0)
local one = FixedPoint.new(1)

local rotationY = 0 -- Player's current angle
local moveSpeed = one / 256

local player = Entity.Find("PlayerModel")

function onCreate(self)
    player = Entity.Find("PlayerModel")
    minPosB = Entity.Find("PointA")
    maxPosA = Entity.Find("PointB")

    local rot = Entity.GetRotationY(player)
    local fourth = one - one / 4

    if rot < zero and rot > -one then
        rotationY = 270
    elseif rot == zero then
        rotationY = 360
    elseif rot > zero and rot < fourth then
        rotationY = 90
    else
        rotationY = 180
    end

    setPlayerRotation()
    SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
end

function setPlayerRotation()
    local rot = one * rotationY / 180
    Entity.SetRotationY(player, rot) -- Rotate object.
end

function onUpdate(self, dt)
    -- Don't move while dialogue is active.
    if isInDialogue() then return end

    -- Player rotate:
    if Input.IsHeldPlayer1(Input.LEFT) then
        rotationY = rotationY - 10
        if rotationY <= 0 then rotationY = 360 end
        setPlayerRotation()
    elseif Input.IsHeldPlayer1(Input.RIGHT) then
        rotationY = rotationY + 10
        if rotationY >= 360 then rotationY = 10 end
        setPlayerRotation()
    end

    -- Player move:
    if Input.IsHeldPlayer1(Input.UP) then
        movePlayer(true)
    elseif Input.IsHeldPlayer1(Input.DOWN) then
        movePlayer(false)
    end
end

function movePlayer(isForward)
    local position = player.position
    local oldX = position.x
    local oldY = position.y
    local oldZ = position.z

    if isForward then
        Entity.MoveForward(player, moveSpeed)
    else
        Entity.MoveBackward(player, moveSpeed)
    end

    position = player.position

    if not isGlobalPlayerInBounds(position) then
        Entity.SetPosition(player, Vec3.new(oldX, oldY, oldZ))
        position = player.position
    end

    Player.SetPosition(position)
end

function onButtonPress(self, button)
    -- Don't move while dialogue is active.
    if isInDialogue() then return end

    -- Player animations:
    if button == Input.UP then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
        -- Set moveSpeed:
        if button == Input.CROSS then moveSpeed = one / 128 end
    elseif button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    end

    if button == Input.CROSS then
        moveSpeed = one / 192
    end

    -- CONSOLE TEST BUTTON:
    if button == Input.TRIANGLE then
        consolePrintGlobalBoundsData()
    end

    -- Level skip button for testing:
    --if button == Input.SELECT then
    --    local scene = Scene.GetIndex() + 1
    --    if scene == 4 then return end
    --    Audio.PauseCDDA()
    --   Scene.Load(scene)
    --end
end

function onButtonRelease(self, button)
    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
    elseif button == Input.CROSS then
        -- Walk speed.
        moveSpeed = one / 256
    end

    if button == Input.CROSS then
        moveSpeed = one / 256
    end
end
