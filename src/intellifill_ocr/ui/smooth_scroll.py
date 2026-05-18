from __future__ import annotations

from PySide6.QtCore import QEasingCurve, QEvent, QObject, QPropertyAnimation, Qt
from PySide6.QtGui import QWheelEvent
from PySide6.QtWidgets import QApplication, QAbstractItemView, QAbstractScrollArea, QGraphicsView, QScrollBar


class SmoothScrollFilter(QObject):
    """App-wide wheel smoothing for normal scroll areas.

    Document preview widgets are skipped because they use the mouse wheel for zoom.
    Ctrl+wheel is also left untouched so future zoom shortcuts continue to work.
    """

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._animations: dict[int, QPropertyAnimation] = {}
        self._targets: dict[int, float] = {}
        self._polished_areas: set[int] = set()

    def eventFilter(self, watched: QObject, event: QEvent) -> bool:  # noqa: N802
        if event.type() != QEvent.Type.Wheel or not isinstance(event, QWheelEvent):
            return False
        if event.modifiers() & Qt.KeyboardModifier.ControlModifier:
            return False

        scroll_area = self._scroll_area_for(watched)
        if scroll_area is None:
            return False

        self.polish_scroll_area(scroll_area)
        horizontal = self._should_scroll_horizontally(event)
        scroll_bar = scroll_area.horizontalScrollBar() if horizontal else scroll_area.verticalScrollBar()
        if scroll_bar.minimum() == scroll_bar.maximum():
            return False

        amount = self._scroll_amount(event, scroll_bar, horizontal)
        if amount == 0:
            return False

        key = id(scroll_bar)
        current_target = self._targets.get(key, float(scroll_bar.value()))
        target_float = self._clamp_float(current_target - amount, scroll_bar.minimum(), scroll_bar.maximum())
        target = int(round(target_float))
        if target == scroll_bar.value():
            self._targets[key] = target_float
            event.accept()
            return True

        self._animate_scroll_bar(scroll_bar, target, target_float)
        event.accept()
        return True

    def polish_scroll_area(self, scroll_area: QAbstractScrollArea) -> None:
        key = id(scroll_area)
        if key in self._polished_areas:
            return
        self._polished_areas.add(key)

        scroll_area.verticalScrollBar().setSingleStep(18)
        scroll_area.horizontalScrollBar().setSingleStep(18)
        if isinstance(scroll_area, QAbstractItemView):
            scroll_area.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerPixel)
            scroll_area.setHorizontalScrollMode(QAbstractItemView.ScrollMode.ScrollPerPixel)

    def _scroll_area_for(self, obj: QObject) -> QAbstractScrollArea | None:
        current: QObject | None = obj
        while current is not None:
            if isinstance(current, QGraphicsView):
                return None
            if isinstance(current, QAbstractScrollArea):
                return current
            current = current.parent()
        return None

    def _animate_scroll_bar(self, scroll_bar: QScrollBar, target: int, target_float: float) -> None:
        key = id(scroll_bar)
        animation = self._animations.get(key)
        if animation is None:
            animation = QPropertyAnimation(scroll_bar, b"value", self)
            animation.setEasingCurve(QEasingCurve.Type.OutCubic)
            animation.finished.connect(lambda key=key: self._targets.pop(key, None))
            scroll_bar.destroyed.connect(lambda _obj=None, key=key: self._cleanup_scroll_bar(key))
            self._animations[key] = animation
        elif animation.state() == QPropertyAnimation.State.Running:
            animation.stop()

        self._targets[key] = target_float
        distance = abs(target - scroll_bar.value())
        animation.setDuration(self._duration_for_distance(distance))
        animation.setStartValue(scroll_bar.value())
        animation.setEndValue(target)
        animation.start()

    def _cleanup_scroll_bar(self, key: int) -> None:
        animation = self._animations.pop(key, None)
        if animation and animation.state() == QPropertyAnimation.State.Running:
            animation.stop()
        self._targets.pop(key, None)

    @staticmethod
    def _should_scroll_horizontally(event: QWheelEvent) -> bool:
        angle_delta = event.angleDelta()
        if event.modifiers() & Qt.KeyboardModifier.ShiftModifier:
            return True
        return abs(angle_delta.x()) > abs(angle_delta.y())

    @staticmethod
    def _scroll_amount(event: QWheelEvent, scroll_bar: QScrollBar, horizontal: bool) -> float:
        pixel_delta = event.pixelDelta()
        if not pixel_delta.isNull():
            delta = pixel_delta.x() if horizontal and pixel_delta.x() else pixel_delta.y()
            return float(delta)

        angle_delta = event.angleDelta()
        delta = angle_delta.x() if horizontal and angle_delta.x() else angle_delta.y()
        if delta == 0:
            return 0.0

        wheel_lines = QApplication.wheelScrollLines() or 3
        single_step = max(14, scroll_bar.singleStep())
        return (delta / 120.0) * wheel_lines * single_step

    @staticmethod
    def _duration_for_distance(distance: int) -> int:
        return max(120, min(220, 110 + distance // 4))

    @staticmethod
    def _clamp(value: int, minimum: int, maximum: int) -> int:
        return max(minimum, min(maximum, value))

    @staticmethod
    def _clamp_float(value: float, minimum: int, maximum: int) -> float:
        return max(float(minimum), min(float(maximum), value))


def install_smooth_scrolling(app: QApplication) -> SmoothScrollFilter:
    existing = getattr(app, "_intellifill_smooth_scroll_filter", None)
    if isinstance(existing, SmoothScrollFilter):
        return existing

    smooth_scroll_filter = SmoothScrollFilter(app)
    app.installEventFilter(smooth_scroll_filter)
    setattr(app, "_intellifill_smooth_scroll_filter", smooth_scroll_filter)
    return smooth_scroll_filter
